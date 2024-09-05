import { AzureFunction, Context, HttpRequest } from "@azure/functions";
import { TableClient } from "@azure/data-tables";
import { Alchemy, Network } from "alchemy-sdk";

const tableStorageConnection = process.env["toolblox_STORAGE"] || "";
const networks = (process.env["alchemy_NETWORKS"] || "").split(",").map(n => n.trim());

const httpTrigger: AzureFunction = async function (context: Context, req: HttpRequest): Promise<void> {
    const tableClient = TableClient.fromConnectionString(tableStorageConnection, "Profile");
    let alchemyApiKey: string;
    const apiKey = req.query.apiKey || req.body?.apiKey;
    if (!apiKey) {
        context.res = {
            status: 400,
            body: "Please provide an API key"
        };
        return;
    }

    let profileId: string;
    try {
        const apiKeyTableClient = TableClient.fromConnectionString(tableStorageConnection, "ApiKeys");
        const apiKeyEntity = await apiKeyTableClient.getEntity("", apiKey);
        profileId = apiKeyEntity.UserId as string;
    } catch (error) {
        context.log.error("Failed to fetch profile ID from API key:", error);
        context.res = {
            status: 401,
            body: "Invalid API key"
        };
        return;
    }

    try {
        const entity = await tableClient.getEntity("", profileId, {
            queryOptions: {
                select: ["AlchemyKey"]
            }
        });
        alchemyApiKey = entity.AlchemyKey.toString();
    } catch (error) {
        context.log.error("Failed to fetch Alchemy API key from table storage:", error);
        throw new Error("Unable to retrieve Alchemy API key");
    }

    if (!alchemyApiKey) {
        throw new Error("Alchemy API key not found in table storage");
    }

    context.log("Alchemy API key retrieved successfully");

    const address = req.query.address || req.body?.address;
    if (!address) {
        context.res = {
            status: 400,
            body: "Please provide a blockchain address"
        };
        return;
    }

    try {
        const nftPromises = networks.map(async (networkName) => {
            let network = networkName as Network;
            const config = {
                apiKey: alchemyApiKey,
                network: network
            };
            const alchemy = new Alchemy(config);
            const nfts = await alchemy.nft.getNftsForOwner(address);
            return { network: networkName, nfts: nfts.ownedNfts };
        });

        const results = await Promise.all(nftPromises);
        const formattedResults = results.flatMap(result => 
            result.nfts.filter(nft => 
                nft.image && 
                nft.image.contentType && 
                nft.image.contentType.startsWith('image/') &&
                nft.image.originalUrl &&
                nft.image.cachedUrl &&
                nft.image.thumbnailUrl
            ).map(nft => ({
                blockchain: (() => {
                    switch (result.network) {
                        case 'eth-mainnet':
                        case 'eth-sepolia':
                            return 'Ethereum';
                        case 'base-mainnet':
                        case 'base-sepolia':
                            return 'Base';
                        case 'arb-mainnet':
                        case 'arb-sepolia':
                            return 'Arbitrum';
                        case 'polygon-mainnet':
                        case 'polygon-amoy':
                            return 'Polygon';
                        case 'opt-mainnet':
                        case 'opt-sepolia':
                            return 'Optimism';
                        case 'zksync-mainnet':
                        case 'zksync-sepolia':
                            return 'ZKsync';
                        default:
                            return 'Unknown';
                    }
                })(),
                network: result.network,
                contentType: nft.image.contentType,
                originalUrl: nft.image.originalUrl,
                cachedUrl: nft.image.cachedUrl,
                thumbnailUrl: nft.image.thumbnailUrl,
                name: nft.name || '',
                contractName: nft.contract.name || ''
            }))
        );

        context.res = {
            status: 200,
            body: formattedResults
        };
    }
    catch (error) {
        context.log.error("Error fetching NFTs:", error);
        context.res = {
            status: 500,
            body: "An error occurred while fetching NFTs"
        };
    }
};

export default httpTrigger;

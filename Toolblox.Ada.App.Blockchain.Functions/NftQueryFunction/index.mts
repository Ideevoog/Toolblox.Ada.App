import { AzureFunction, Context, HttpRequest } from "@azure/functions";
import { Alchemy, Network } from "alchemy-sdk";
import { getProfileIdFromRequest, getAlchemyConfiguration } from '../lib/helpers.mjs';

const networks = (process.env["alchemy_NETWORKS"] || "").split(",").map(n => n.trim());

const httpTrigger: AzureFunction = async function (context: Context, req: HttpRequest): Promise<void> {
    const profileId = await getProfileIdFromRequest(req, context);
    if (!profileId) {
        return;
    }
    const alchemyApiKey = (await getAlchemyConfiguration(profileId, context))?.apiKey;
    if (!alchemyApiKey) {
        throw new Error("Alchemy API key not found or unable to retrieve");
    }
    const address = req.query.address || req.body?.address;
    if (!address) {
        context.res = {
            status: 400,
            body: "Please provide a blockchain address"
        };
        return;
    }

    try {
        const results = [];
        for (const networkName of networks) {
            let network = networkName as Network;
            const config = {
                apiKey: alchemyApiKey,
                network: network
            };
            const alchemy = new Alchemy(config);
            const nfts = await alchemy.nft.getNftsForOwner(address);
            results.push({ network: networkName, nfts: nfts.ownedNfts });
            
            // Wait for 1 second plus 200ms before the next iteration
            await new Promise(resolve => setTimeout(resolve, 1200));
        }
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

import { AzureFunction, Context, HttpRequest } from "@azure/functions";
import { TableClient } from "@azure/data-tables";
import * as ethers from 'ethers';
import * as jwt from 'jsonwebtoken';
import * as jwksRsa from 'jwks-rsa';
import * as nearAPI from 'near-api-js';
const { keyStores, connect } = nearAPI;
const myKeyStore = new keyStores.InMemoryKeyStore();

const tableStorageConnection = process.env["toolblox_STORAGE"] || "";

import jwksClient from 'jwks-rsa';
var client = jwksClient({
  jwksUri: 'https://toolblox.eu.auth0.com/.well-known/jwks.json'
});

function getKey(header, callback){
  client.getSigningKey(header.kid, function(err, key : any) {
    var signingKey = key.publicKey || key.rsaPublicKey;
    callback(null, signingKey);
  });
}

const httpTrigger: AzureFunction = async function (context: Context, req: HttpRequest): Promise<void> {
    var bearer = (req.headers.authorization ?? "").replace("Bearer ", "");
    var login = () => new Promise<string>((resolve) => 
        {
            jwt.verify(
                bearer,
                getKey,
                { audience: 'http://localhost:7071/api/Function1', issuer: 'https://toolblox.eu.auth0.com/' },
                function(err, decoded) {
                    console.log("decoded: " + JSON.stringify(decoded));
                    if (err)
                    {
                        throw err;
                    }
                    resolve(decoded.sub as any);
                });
        });
    var userId = await login();

    const island = req.query.island;

    const client = TableClient.fromConnectionString(tableStorageConnection, `Subscriptions`);
    var subscription : Subscription;
    try{
        subscription = await client.getEntity<Subscription>(userId, userId + ":" + island);
    } catch {}
    if (subscription == undefined) {
        subscription = {
            partitionKey: userId,
            rowKey: userId + ":" + island,
            SelectedChain: island.includes("Near") ? 1
                : (island.includes("Polygon") ? 2
                : (island.includes("Aurora") ? 3
                : (island.includes("Avalanche") ? 4
                : (island.includes("Evmos") ? 5
                : (island.includes("Ethereum") ? 6
                : (island.includes("Binance") ? 7 : 0)))))),
            SelectedBlockchainKind: island.includes("Testnet") ? 0 : 1,
            ValidUntil: { value: "0", type: "Int64" }
        }
    }
    //const timeSeconds = Math.floor(new Date().getTime() / 1000);
    
    try {
        if (subscription.SelectedChain == 1) {
            const connectionConfig = subscription.SelectedBlockchainKind == 0
            ? {
                networkId: "testnet",
                keyStore: myKeyStore,
                nodeUrl: "https://rpc.testnet.near.org",
                walletUrl: "https://wallet.testnet.near.org",
                helperUrl: "https://helper.testnet.near.org",
                explorerUrl: "https://explorer.testnet.near.org",
            } : {
                networkId: "mainnet",
                keyStore: myKeyStore,
                nodeUrl: "https://rpc.mainnet.near.org",
                walletUrl: "https://wallet.mainnet.near.org",
                helperUrl: "https://helper.mainnet.near.org",
                explorerUrl: "https://explorer.mainnet.near.org",
            };
            let contractAddress = "TESTNET";
            if (subscription.SelectedBlockchainKind == 1)
            {
                contractAddress = "MAINNET";
            }
            const nearConnection = await connect(connectionConfig);
            const contract = new nearAPI.Contract(await nearConnection.account(contractAddress), contractAddress, {
                viewMethods: ['getItemIdByExternalId', 'getValidUntil'],
                changeMethods: []
            });
            // @ts-ignore
            var itemId = await contract.getItemIdByExternalId({ "externalId": userId });
            // @ts-ignore
            var validUntil = await contract.getValidUntil({ "id": itemId });
            subscription.ValidUntil = { value: validUntil.toString(), type: "Int64" };
            console.log("Found valid until for user: " + userId + ", sub:" + itemId + ", subscription: " + subscription);
        } else {
            let network = "";
            let contractAddress = "";
            switch (subscription.SelectedChain) {
                case 0:
                throw "No selected chain!";
                case 2:
                    network = subscription.SelectedBlockchainKind == 0
                        ? "https://rpc-mumbai.maticvigil.com/"
                        : "https://polygon-rpc.com/";
                    contractAddress = subscription.SelectedBlockchainKind == 0
                        ? ""
                        : "";
                break;
                case 3:
                    network = subscription.SelectedBlockchainKind == 0
                        ? "https://testnet.aurora.dev"
                        : "https://mainnet.aurora.dev";
                    contractAddress = subscription.SelectedBlockchainKind == 0
                        ? ""
                        : "0xE0c0085F879aB55Def44b164021e576DA9acd0f0";
                break;
                case 4:
                    network = subscription.SelectedBlockchainKind == 0
                        ? "https://api.avax-test.network/ext/bc/C/rpc"
                        : "https://api.avax.network/ext/bc/C/rpc";
                    contractAddress = subscription.SelectedBlockchainKind == 0
                        ? ""
                        : "";
                break;
                case 5:
                    network = subscription.SelectedBlockchainKind == 0
                        ? "https://eth.bd.evmos.dev:8545"
                        : "https://eth.bd.evmos.org:8545";
                    contractAddress = subscription.SelectedBlockchainKind == 0
                        ? ""
                        : "";
                break;
                case 6:
                    network = subscription.SelectedBlockchainKind == 0
                        ? "https://rpc.sepolia.dev"
                        : "https://mainnet.infura.io/v3/";
                    contractAddress = subscription.SelectedBlockchainKind == 0
                        ? ""
                        : "";
                    console.log("using ethereum: " + network + ", contract: " + contractAddress);
                break;
                case 7:
                    network = subscription.SelectedBlockchainKind == 0
                        ? "https://data-seed-prebsc-2-s2.binance.org:8545/"
                        : "https://bsc-dataseed.binance.org/";
                    contractAddress = subscription.SelectedBlockchainKind == 0
                        ? ""
                        : "";
                    console.log("using ethereum: " + network + ", contract: " + contractAddress);
                break;
                case 1:
                default:
                    break;
            }
            const provider = new ethers.providers.JsonRpcProvider(network);
            await provider.ready;
            const contract = new ethers.Contract(contractAddress, [{"inputs":[],"stateMutability":"nonpayable","type":"constructor"},{"anonymous":false,"inputs":[{"indexed":false,"internalType":"uint256","name":"_id","type":"uint256"}],"name":"ItemUpdated","type":"event"},{"stateMutability":"payable","type":"fallback"},{"inputs":[{"internalType":"uint256","name":"feeTier","type":"uint256"},{"internalType":"string","name":"externalId","type":"string"}],"name":"create","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"nonpayable","type":"function"},{"inputs":[{"internalType":"address","name":"","type":"address"}],"name":"customerList","outputs":[{"internalType":"bool","name":"","type":"bool"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getCustomer","outputs":[{"internalType":"address","name":"","type":"address"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getExternalId","outputs":[{"internalType":"string","name":"","type":"string"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getId","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getItem","outputs":[{"components":[{"internalType":"uint256","name":"id","type":"uint256"},{"internalType":"uint64","name":"status","type":"uint64"},{"internalType":"string","name":"name","type":"string"},{"internalType":"address","name":"customer","type":"address"},{"internalType":"uint256","name":"validUntil","type":"uint256"},{"internalType":"string","name":"externalId","type":"string"},{"internalType":"string","name":"publicKey","type":"string"}],"internalType":"struct SubscriptionWorkflow.Subscription","name":"","type":"tuple"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"address","name":"customer","type":"address"}],"name":"getItemByCustomer","outputs":[{"components":[{"internalType":"uint256","name":"id","type":"uint256"},{"internalType":"uint64","name":"status","type":"uint64"},{"internalType":"string","name":"name","type":"string"},{"internalType":"address","name":"customer","type":"address"},{"internalType":"uint256","name":"validUntil","type":"uint256"},{"internalType":"string","name":"externalId","type":"string"},{"internalType":"string","name":"publicKey","type":"string"}],"internalType":"struct SubscriptionWorkflow.Subscription","name":"","type":"tuple"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"string","name":"externalId","type":"string"}],"name":"getItemByExternalId","outputs":[{"components":[{"internalType":"uint256","name":"id","type":"uint256"},{"internalType":"uint64","name":"status","type":"uint64"},{"internalType":"string","name":"name","type":"string"},{"internalType":"address","name":"customer","type":"address"},{"internalType":"uint256","name":"validUntil","type":"uint256"},{"internalType":"string","name":"externalId","type":"string"},{"internalType":"string","name":"publicKey","type":"string"}],"internalType":"struct SubscriptionWorkflow.Subscription","name":"","type":"tuple"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"address","name":"customer","type":"address"}],"name":"getItemIdByCustomer","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"string","name":"externalId","type":"string"}],"name":"getItemIdByExternalId","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"cnt","type":"uint256"}],"name":"getLatest","outputs":[{"components":[{"internalType":"uint256","name":"id","type":"uint256"},{"internalType":"uint64","name":"status","type":"uint64"},{"internalType":"string","name":"name","type":"string"},{"internalType":"address","name":"customer","type":"address"},{"internalType":"uint256","name":"validUntil","type":"uint256"},{"internalType":"string","name":"externalId","type":"string"},{"internalType":"string","name":"publicKey","type":"string"}],"internalType":"struct SubscriptionWorkflow.Subscription[]","name":"","type":"tuple[]"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getName","outputs":[{"internalType":"string","name":"","type":"string"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"cursor","type":"uint256"},{"internalType":"uint256","name":"howMany","type":"uint256"},{"internalType":"bool","name":"onlyMine","type":"bool"}],"name":"getPage","outputs":[{"components":[{"internalType":"uint256","name":"id","type":"uint256"},{"internalType":"uint64","name":"status","type":"uint64"},{"internalType":"string","name":"name","type":"string"},{"internalType":"address","name":"customer","type":"address"},{"internalType":"uint256","name":"validUntil","type":"uint256"},{"internalType":"string","name":"externalId","type":"string"},{"internalType":"string","name":"publicKey","type":"string"}],"internalType":"struct SubscriptionWorkflow.Subscription[]","name":"","type":"tuple[]"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getPublicKey","outputs":[{"internalType":"string","name":"","type":"string"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getStatus","outputs":[{"internalType":"uint64","name":"","type":"uint64"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getValidUntil","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"","type":"uint256"}],"name":"items","outputs":[{"internalType":"uint256","name":"id","type":"uint256"},{"internalType":"uint64","name":"status","type":"uint64"},{"internalType":"string","name":"name","type":"string"},{"internalType":"address","name":"customer","type":"address"},{"internalType":"uint256","name":"validUntil","type":"uint256"},{"internalType":"string","name":"externalId","type":"string"},{"internalType":"string","name":"publicKey","type":"string"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"address","name":"","type":"address"}],"name":"itemsByCustomer","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"string","name":"","type":"string"}],"name":"itemsByExternalId","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"view","type":"function"},{"inputs":[],"name":"owner","outputs":[{"internalType":"address","name":"","type":"address"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"remove","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"nonpayable","type":"function"},{"inputs":[{"internalType":"address","name":"_newOwner","type":"address"}],"name":"setOwner","outputs":[],"stateMutability":"nonpayable","type":"function"},{"inputs":[],"name":"token","outputs":[{"internalType":"address","name":"","type":"address"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"},{"internalType":"uint256","name":"feeTier","type":"uint256"},{"internalType":"string","name":"externalId","type":"string"}],"name":"topUp","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"nonpayable","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"},{"internalType":"string","name":"publicKey","type":"string"}],"name":"updateKey","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"nonpayable","type":"function"},{"stateMutability":"payable","type":"receive"}], provider);
            const subscriptionId = await contract.getItemIdByExternalId(userId);
            subscription.ValidUntil = { value: ((await contract.getValidUntil(subscriptionId))?.toNumber() ?? 0).toString(), type: "Int64" };
            console.log("Found valid until for user: " + userId + ", sub:" + subscriptionId + ", subscription: " + JSON.stringify(subscription));
        }
        await client.upsertEntity(subscription, "Merge");
        console.log("Returning " + subscription.ValidUntil.value);
        context.res = {
            status: 200,
            body: {
                island : island,
                validUntil : Number(subscription.ValidUntil.value)
            },
            headers: {
                'Content-Type': 'application/json'
            }
        }
    }
    catch (error) {
        throw error;
    }
};

interface Subscription {
    partitionKey: string;
    rowKey: string;
    SelectedChain: number;
    SelectedBlockchainKind: number;
    ValidUntil: { value: string, type: string};
}
export default httpTrigger;

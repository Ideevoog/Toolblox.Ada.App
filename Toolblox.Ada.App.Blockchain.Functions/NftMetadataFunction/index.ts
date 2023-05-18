import { AzureFunction, Context, HttpRequest } from "@azure/functions";
import { TableClient } from "@azure/data-tables";
import * as ethers from 'ethers';
const nearAPI = require("near-api-js");
const { keyStores, KeyPair, connect } = nearAPI;
const myKeyStore = new keyStores.InMemoryKeyStore();

const tableStorageConnection = process.env["toolblox_STORAGE"] || "";

const httpTrigger: AzureFunction = async function (context: Context, req: HttpRequest): Promise<void> {
    const itemId = req.query.id;
    const workflowUrl = req.query.workflowId.replace(/['"-]/g, "");
    console.log("=============== req initiated for item: " + itemId + ", workflow: " + workflowUrl + ", url: " + req.url + ", query: " + req.query);
    const client = TableClient.fromConnectionString(tableStorageConnection, `Workflows`);
    const workflowList = client.listEntities<Workflow>({
        queryOptions: {
            filter: `Url eq '${workflowUrl}'`,
            select: [ "Project", "Object", "SelectedChain", "SelectedBlockchainKind", "NearTestnet", "NearMainnet", "Abi" ]
        },
    });
    let workflow: Workflow = undefined;
    for await (const workflowLine of workflowList) {
        workflow = workflowLine;
        break;
    }
    if (workflow == undefined) {
        throw new Error('Cannot find workflow with url ' + workflowUrl);
    }
    //console.log("++++++++++ WORKFLOW: " + JSON.stringify(workflow));
    //const workflow = await client.getEntity<Workflow>("testnet", req.query.workflowId);
    // adds the keyPair you created to keyStore
    let cid = "";
    let name = "";
    let description = "";
    let contractAddress = workflow.NearTestnet;
    let traits = [];
    if (workflow.SelectedBlockchainKind == 1)
    {
        contractAddress = workflow.NearMainnet;
    }
    try {
        if (workflow.SelectedChain == 1) {
            const connectionConfig = workflow.SelectedBlockchainKind == 0
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
            const nearConnection = await connect(connectionConfig);
            const contract = new nearAPI.Contract(await nearConnection.account(contractAddress), contractAddress, {
                viewMethods: ['getItem'],
                changeMethods: []
            });
            var item = await contract.getItem({ "id": itemId });
            cid = item.image;
            name = item.name;
            description = item.description;
        } else {
            let network = "";
            switch (workflow.SelectedChain) {
                case 0:
                throw "No selected chain!";
                case 2:
                    network = workflow.SelectedBlockchainKind == 0
                        ? "https://rpc-mumbai.maticvigil.com/"
                        : "https://polygon-rpc.com/";
                break;
                case 3:
                    network = workflow.SelectedBlockchainKind == 0
                        ? "https://testnet.aurora.dev"
                        : "https://mainnet.aurora.dev";
                break;
                case 4:
                    network = workflow.SelectedBlockchainKind == 0
                        ? "https://api.avax-test.network/ext/bc/C/rpc"
                        : "https://api.avax.network/ext/bc/C/rpc";
                break;
                case 5:
                    network = workflow.SelectedBlockchainKind == 0
                        ? "https://eth.bd.evmos.dev:8545"
                        : "https://eth.bd.evmos.org:8545";
                break;
                case 6:
                    network = workflow.SelectedBlockchainKind == 0
                        ? "https://rpc.sepolia.org"
                        : "https://mainnet.infura.io/v3/";
                break;
                case 7:
                    network = workflow.SelectedBlockchainKind == 0
                        ? "https://data-seed-prebsc-2-s2.binance.org:8545/"
                        : "https://bsc-dataseed.binance.org/";
                case 1:
                default:
                    break;
            }
            //const abi = JSON.parse('[{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getId","outputs":[{"internalType":"uint256","name":"","type":"uint256"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getName","outputs":[{"internalType":"string","name":"","type":"string"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getDescription","outputs":[{"internalType":"string","name":"","type":"string"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getImage","outputs":[{"internalType":"string","name":"","type":"string"}],"stateMutability":"view","type":"function"},{"inputs":[{"internalType":"uint256","name":"id","type":"uint256"}],"name":"getStatus","outputs":[{"internalType":"uint64","name":"","type":"uint64"}],"stateMutability":"view","type":"function"}]');
            console.log("================ contract: " + contractAddress + ", workflowId : " + req.query.workflowId + ", network: " + network + ", itemId = " + itemId)
            //console.log("================ abi to parse: " + workflow.Abi);
            const abi = JSON.parse(workflow.Abi);
            const contract = new ethers.Contract(contractAddress, abi, new ethers.providers.JsonRpcProvider(network));
            // var methods = abi
            //     .filter(method => method.inputs !== undefined && method.inputs.length == 1 && method.inputs[0].name === "id")
            //     .filter(method => method.outputs !== undefined && method.outputs.length == 1 && method.outputs[0].components === undefined)
            //     .filter(method => method.name.startsWith("get"))
            //     .map(method => method.name);

            // for (let i = 0; i < methods.length; i++) {
            //     traits.push({
            //         trait_type : methods[i].name.replace(/get([A-Z])/g, ' $1').trim(),
            //         value: await await contract[methods[i].name].apply(contract, [ itemId ]),
            //     });
            // }

            var traitNames = abi
                .filter(method => method.name === "getItem")
                .flatMap(method => method.outputs[0].components)
                .map(prop => prop.name.replace(/([A-Z])/g, ' $1').trim())
                .map(prop => prop.charAt(0).toUpperCase() + prop.slice(1));
            var traitValues = await contract.getItem(itemId);

            for (let i = 0; i < traitNames.length; i++) {
                traits.push({
                    trait_type : traitNames[i],
                    value: traitValues[i],
                });
            }
            //console.log(JSON.stringify(traits));

            name = traits.filter(t => t.trait_type == "Name")[0].value;
            cid = traits.filter(t => t.trait_type == "Image")[0].value;
            description = traits.filter(t => t.trait_type == "Description")[0].value;

            console.log("================== got " + name +", cid" + cid);
        }
        context.res = {
            status: 200, /* Defaults to 200 */
            body: {
                name : name,
                description : description,
                image : "ipfs://" + cid,
                external_link : "https://app.toolblox.net/flow/" + req.query.workflowId + "/" + itemId,
                attributes: traits
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

interface Workflow {
    partitionKey: string;
    rowKey: string;
    Abi?: string;
    Project: string;
    Object: string;
    SelectedChain: number;
    SelectedBlockchainKind: number;
    NearTestnet?: string;
    NearMainnet?: string;
}
export default httpTrigger;

import { AzureFunction, Context, HttpRequest } from "@azure/functions";

import { getProfileIdFromRequest, getAlchemyConfiguration, UserOperationContext } from '../lib/helpers.mjs';
import { TableClient } from "@azure/data-tables";
import { ethers } from "ethers";

import { createAlchemySmartAccountClient, baseSepolia } from "@account-kit/infra";
import { createLightAccount } from "@account-kit/smart-contracts";

import { LocalAccountSigner, UserOperationRequest, UserOperationRequest_v7, UserOperationRequest_v6,   deepHexlify,
    isValidRequest,
    resolveProperties} from "@aa-sdk/core";
import { http, RpcTransactionRequest, Hex, toHex } from "viem";

import { LocalAccount } from "viem/accounts";
import { error } from "console";

const httpTrigger: AzureFunction = async function (context: Context, req: HttpRequest): Promise<void> {
    const profileId = await getProfileIdFromRequest(req, context);
    if (!profileId) {
        return;
    }
    const alchemyConfig = await getAlchemyConfiguration(profileId, context);
    const alchemyApiKey = alchemyConfig?.apiKey;
    if (!alchemyApiKey) {
        throw new Error("Alchemy API key not found or unable to retrieve");
    }
    // Get the user operation from the request body
    const userOperations = req.body;

    console.log(JSON.stringify(userOperations));

    if (!userOperations) {
        context.res = {
            status: 400,
            body: "Please provide a user operation in the request body"
        };
        return;
    }

    /*
        TODO: process user operation. example:
        userOperations = [
            {
                "id": "aaaa-bbbb-cccc",
                "workflow": "some_example_workflow",
                "method": "mint",
                "from" : "0x0",
                "parameters": {
                    "to": "0x123",
                    "amount": "100"
                }
            },
            {
                "id": "aaaa-bbbb-cccc",
                "workflow": "some_example_workflow",
                "method": "mint",
                "from" : "0x0",
                "parameters": {
                    "to": "0x123",
                    "amount": "100"
                }
            }
        ]
    */
    const tableStorageConnection = process.env["toolblox_STORAGE"] || "";
    const workflowsTableClient = TableClient.fromConnectionString(tableStorageConnection, "Workflows");
    const userOperationContexts: UserOperationContext[] = [];
    // Group userOperations by 'from' address
    const groupedOperations = userOperations.reduce((acc, op) => {
        if (!acc[op.from]) {
            acc[op.from] = [];
        }
        acc[op.from].push(op);
        return acc;
    }, {} as Record<string, typeof userOperations>);

    for (const [from, operations] of Object.entries(groupedOperations)) {
        try {
            const localAccount: LocalAccount = {
                address: from as `0x${string}`,
                publicKey: '0xYourPublicKey' as Hex,
                source: 'customSource',
                type: 'local',
                signMessage: async (args) => {
                    // Implement your custom signMessage logic here
                    return '0xYourSignature' as Hex;
                },
                signTransaction: async (args) => {
                    // Implement your custom signTransaction logic here
                    return '0xYourTransactionSignature' as Hex;
                },
                signTypedData: async (args) => {
                    // Implement your custom signTypedData logic here
                    return '0xYourTypedDataSignature' as Hex;
                },
            };
            const templateAccount =  await createLightAccount({
                chain: baseSepolia,
                transport: http(`${baseSepolia.rpcUrls.alchemy.http[0]}/krtsKzom1MUv5MG3LHuYzw82NgZGprBC`),
                signer: new LocalAccountSigner(localAccount),
            });
            const entryPoint = templateAccount.getEntryPoint();

            const client = createAlchemySmartAccountClient({
                apiKey: "krtsKzom1MUv5MG3LHuYzw82NgZGprBC",//alchemyApiKey,
                policyId: alchemyConfig.policyId,
                chain: baseSepolia,
                useSimulation: false,
                account : templateAccount,
            });
            const account = client.account;

            const requests: RpcTransactionRequest[] = [];

            for (const userOperation of operations as typeof userOperations) {
                const workflowEntity = await fetchWorkflowEntity(workflowsTableClient, userOperation.workflow, profileId);
                if (!workflowEntity) {
                    throw new Error(`Workflow not found: ${userOperation.workflow}`);
                }
                const contractAbi = JSON.parse(workflowEntity.Abi);
                const contractAddress = GetAddress(workflowEntity);

                // Encode function call with method and parameters
                const iface = new ethers.utils.Interface(contractAbi);
                const callData = iface.encodeFunctionData(userOperation.method, Object.values(userOperation.parameters));

                // Build transaction request
                requests.push({
                    from: from as `0x${string}`,
                    to: contractAddress as `0x${string}`,
                    data: callData as `0x${string}`,
                    value: userOperation.value || "0x0"
                });
            }

            // This runs all middleware (e.g., gas estimation, paymaster checks)
            const userOp = await client.buildUserOperationFromTxs({ account, requests,
                /*overrides : {
                    maxFeePerGas: 10000,
                    maxPriorityFeePerGas: 10000,
                    callGasLimit: 10000,
                    preVerificationGas: 10000,
                    verificationGasLimit: 10000,
                }*/
            });
              
            const request = deepHexlify(userOp);

            // Generate the user operation hash (to be compared and signed)
            const userOpHash = await entryPoint.getUserOperationHash(request.uoStruct);
            
            userOperationContexts.push({
                userOperation: request,
                hash: userOpHash,
                error: null,
                ids: Array.isArray(operations) ? operations.map(op => op.id as string) : [],
                txHash: null,
            });
        } catch (error) {
            userOperationContexts.push({
                userOperation: {},
                hash: '0x' as `0x${string}`,
                error: `Error processing operations for address ${from}: ${error.message}`,
                ids: Array.isArray(operations) ? operations.map(op => op.id as string) : [],
                txHash: null,
            });
        }
    }

    const successfulOperations = userOperationContexts.filter(context => !context.error);
    const failedOperations = userOperationContexts.filter(context => context.error);

    context.res = {
        status: 200,
        body: {
            message: "User operations processed",
            successfulOperations,
            failedOperations
        }
    };
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

function GetAddress(workflow: Workflow): string | undefined {
    return workflow.SelectedBlockchainKind === 0 ? workflow.NearTestnet : workflow.NearMainnet;
}

// Define the convenience method outside of the loop
async function fetchWorkflowEntity(tableClient: TableClient, workflowUrl: string, userId: string): Promise<Workflow | undefined> {
    const workflowList = tableClient.listEntities<Workflow>({
        queryOptions: {
            filter: `Url eq '${workflowUrl}' and User eq '${userId}'`,
            select: [ "Abi", "partitionKey", "rowKey", "Project", "Object", "SelectedChain", "SelectedBlockchainKind", "NearTestnet", "NearMainnet", "Url", "User" ]
        },
    });
    return (await workflowList.next())?.value;
}

export default httpTrigger;
import { AzureFunction, Context, HttpRequest } from "@azure/functions";

import { fetchWorkflowEntity, GetAddress, getProfileIdFromRequest, getAlchemyConfiguration, UserOperationContext, createAlchemySmartAccountClientWithConfig } from '../lib/helpers.mjs';
import { TableClient } from "@azure/data-tables";
import { ethers } from "ethers";

import { deepHexlify } from "@aa-sdk/core";
import { RpcTransactionRequest } from "viem";

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
            const { client, entryPoint } = await createAlchemySmartAccountClientWithConfig(alchemyConfig, from);
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
            const userOp = await client.buildUserOperationFromTxs({ account, requests });
            const request = deepHexlify(userOp);

            // Generate the user operation hash (to be compared and signed)
            const userOpHash = await entryPoint.getUserOperationHash(request.uoStruct);
            const userOpPacked = await entryPoint.packUserOperation(request.uoStruct);

            userOperationContexts.push({
                userOperation: request,
                hash: userOpHash,
                error: null,
                ids: Array.isArray(operations) ? operations.map(op => op.id as string) : [],
                txHash: null,
                entryPointAddress: entryPoint.address,
                uoPacked: userOpPacked,
                from: from as `0x${string}`
            });
        } catch (error) {
            userOperationContexts.push({
                userOperation: {},
                hash: '0x' as `0x${string}`,
                error: `Error processing operations for address ${from}: ${error.message}. Stack trace: ${error.stack}`,
                ids: Array.isArray(operations) ? operations.map(op => op.id as string) : [],
                txHash: null,
                entryPointAddress: null,
                uoPacked: null,
                from: null
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


export default httpTrigger;
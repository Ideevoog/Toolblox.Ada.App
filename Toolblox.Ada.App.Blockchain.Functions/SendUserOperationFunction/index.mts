import { AzureFunction, Context, HttpRequest } from "@azure/functions";
import { getProfileIdFromRequest, getAlchemyConfiguration, createAlchemySmartAccountClientWithConfig } from '../lib/helpers.mjs';

const httpTrigger: AzureFunction = async function (context: Context, req: HttpRequest): Promise<void> {
    const profileId = await getProfileIdFromRequest(req, context);
    if (!profileId) {
        context.res = {
            status: 400,
            body: "Profile ID not found"
        };
        return;
    }

    const alchemyConfig = await getAlchemyConfiguration(profileId, context);
    const alchemyApiKey = alchemyConfig?.apiKey;
    if (!alchemyApiKey) {
        context.res = {
            status: 500,
            body: "Alchemy API key not found or unable to retrieve"
        };
        return;
    }
    const userOperationContext = req.body;

    if (!userOperationContext || typeof userOperationContext !== 'object') {
        context.res = {
            status: 400,
            body: "Please provide a single UserOperationContext object in the request body"
        };
        return;
    }

    try {
        const { operation, signature, from } = userOperationContext;

        const { client } = await createAlchemySmartAccountClientWithConfig(alchemyConfig, from);
        var account = client.account;

        const entryPointAddress = account.getEntryPoint().address;

        // Ensure real signature is set
        operation.uoStruct.signature = signature;
        
        const hash = await client.sendRawUserOperation(
            operation.uoStruct,
            entryPointAddress
        );

        let txHash;
        try {
            // Wait for the operation to be mined
            txHash = await client.waitForUserOperationTransaction({ hash });
        } catch (e) {
            // If it fails, attempt drop-and-replace
            const { hash: newHash } = await client.dropAndReplaceUserOperation({
                uoToDrop: operation.uoStruct,
                account: client.account,
            });
            txHash = await client.waitForUserOperationTransaction({ hash: newHash });
        }

        context.res = {
            status: 200,
            body: {
                message: "User operation processed successfully",
                hash: txHash
            }
        };
    } catch (error) {
        context.res = {
            status: 200,
            body: {
                message: "Error processing user operation",
                error: error.message
            }
        };
    }
};

export default httpTrigger;
import { AzureFunction, Context, HttpRequest } from "@azure/functions";
import { getProfileIdFromRequest, getAlchemyConfiguration, UserOperationContext } from '../lib/helpers.mjs';
import { createAlchemySmartAccountClient, baseSepolia } from "@account-kit/infra";
import { createLightAccount } from "@account-kit/smart-contracts";
import { http } from "viem";
import { generatePrivateKey } from "viem/accounts";
import { LocalAccountSigner } from "@aa-sdk/core";

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
    const userOperationContexts: UserOperationContext[] = req.body.userOperationContexts;

    if (!userOperationContexts || !Array.isArray(userOperationContexts)) {
        context.res = {
            status: 400,
            body: "Please provide an array of UserOperationContext in the request body"
        };
        return;
    }

    const client = createAlchemySmartAccountClient({
        apiKey: alchemyApiKey,
        policyId: alchemyConfig.policyId,
        chain: baseSepolia,
        account: await createLightAccount({
            chain: baseSepolia,
            transport: http(`${baseSepolia.rpcUrls.alchemy.http[0]}/${alchemyApiKey}`),
            signer: LocalAccountSigner.privateKeyToAccountSigner(generatePrivateKey()),
        }),
    });
    var account = client.account;

    const entryPointAddress  = account.getEntryPoint().address;
    const results: UserOperationContext[] = [];

    for (const context of userOperationContexts) {
        try {
            const { userOperation } = context;
            
            const hash = await client.sendRawUserOperation(
                userOperation,
                entryPointAddress
            );

            try {
                // Wait for the operation to be mined
                const txHash = await client.waitForUserOperationTransaction({ hash });
                results.push({ ...context, txHash });
            } catch (e) {
                // If it fails, attempt drop-and-replace
                const { hash: newHash } = await client.dropAndReplaceUserOperation({
                    uoToDrop: userOperation,
                    account: client.account,
                });
                const newTxHash = await client.waitForUserOperationTransaction({ hash: newHash });
                results.push({ ...context, txHash: newTxHash });
            }
        } catch (error) {
            results.push({ ...context, error: error.message });
        }

        // Add a 2-second delay if there are more operations to process
        //TODO: add to alchemy config
        if (alchemyConfig.delay > 0
            && userOperationContexts.indexOf(context) < userOperationContexts.length - 1) {
            await new Promise(resolve => setTimeout(resolve, alchemyConfig.delay));
        }
    }

    context.res = {
        status: 200,
        body: {
            message: "User operations processed",
            results
        }
    };
};

export default httpTrigger;
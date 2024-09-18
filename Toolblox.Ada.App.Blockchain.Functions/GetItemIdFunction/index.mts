import { AzureFunction, Context, HttpRequest } from "@azure/functions";
import { TableClient } from "@azure/data-tables";
import { ethers } from "ethers";
import { baseSepolia } from "@account-kit/infra";
import { fetchWorkflowEntity, GetAddress, getProfileIdFromRequest, getAlchemyConfiguration } from '../lib/helpers.mjs';

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
    const getItemIdContext = req.body;

    if (!getItemIdContext || typeof getItemIdContext !== 'object') {
        context.res = {
            status: 400,
            body: "Please provide a getItemById request object in the request body"
        };
        return;
    }
    /*
        parameter can be:
        {
            "name": "someNameIsThis",
            "value": "someValue"
        }
    */

    const { workflow, parameter } = getItemIdContext;
    
    try {
        const tableStorageConnection = process.env["toolblox_STORAGE"] || "";
        const workflowsTableClient = TableClient.fromConnectionString(tableStorageConnection, "Workflows");
        const workflowEntity = await fetchWorkflowEntity(workflowsTableClient, workflow, profileId);
        if (!workflowEntity) {
            throw new Error(`Workflow not found: ${workflow}`);
        }

        const contractAbi = JSON.parse(workflowEntity.Abi);
        const contractAddress = GetAddress(workflowEntity);

        // Implement get item id logic using ethers version 6
        const provider = new ethers.providers.JsonRpcProvider(`${baseSepolia.rpcUrls.alchemy.http[0]}/${alchemyApiKey}`);
        const contract = new ethers.Contract(contractAddress, contractAbi, provider);

        // Construct method name
        const methodName = `getItemIdBy${parameter.name.charAt(0).toUpperCase() + parameter.name.slice(1)}`;

        // Call the method with the parameter
        const itemId = await contract[methodName](parameter.value);

        context.res = {
            status: 200,
            body: {
                message: "User operation processed successfully",
                id: itemId.toString() // Convert BigNumber to string
            }
        };
    } catch (error) {
        context.res = {
            status: 200,
            body: {
                message: "Error processing get operation",
                error: error.message
            }
        };
    }
};

export default httpTrigger;
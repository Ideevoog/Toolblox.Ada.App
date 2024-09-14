import { HttpRequest, Context } from "@azure/functions";
import { TableClient } from "@azure/data-tables";

const tableStorageConnection = process.env["toolblox_STORAGE"] || "";

export async function getProfileIdFromRequest(req: HttpRequest, context: Context): Promise<string | null> {
    const apiKey = req.query.apiKey || req.body?.apiKey;
    if (!apiKey) {
        context.res = {
            status: 400,
            body: "Please provide an API key"
        };
        return null;
    }

    try {
        const apiKeyTableClient = TableClient.fromConnectionString(tableStorageConnection, "ApiKeys");
        const apiKeyEntity = await apiKeyTableClient.getEntity("", apiKey);
        return apiKeyEntity.UserId as string;
    } catch (error) {
        context.log.error("Failed to fetch profile ID from API key:", error);
        context.res = {
            status: 401,
            body: "Invalid API key"
        };
        return null;
    }
}

export async function getAlchemyConfiguration(profileId: string, context: Context): Promise<{ apiKey: string | null, policyId: string | null, delay: number }> {
    try {
        const tableClient = TableClient.fromConnectionString(tableStorageConnection, "Profile");
        const entity = await tableClient.getEntity("", profileId, {
            queryOptions: {
                select: ["AlchemyKey", "AlchemyPolicyId"]
            }
        });
        return {
            apiKey: entity.AlchemyKey?.toString() || null,
            policyId: entity.AlchemyPolicyId?.toString() || null,
            delay: 2000
        };
    } catch (error) {
        context.log.error("Failed to fetch Alchemy configuration from table storage:", error);
        return { apiKey: null, policyId: null, delay: 2000 };
    }
}

export const LightAccountFactoryAbi_v2 = [
    {
      type: "constructor",
      inputs: [
        { name: "owner", type: "address", internalType: "address" },
        {
          name: "entryPoint",
          type: "address",
          internalType: "contract IEntryPoint",
        },
      ],
      stateMutability: "nonpayable",
    },
    { type: "receive", stateMutability: "payable" },
    {
      type: "function",
      name: "ACCOUNT_IMPLEMENTATION",
      inputs: [],
      outputs: [
        {
          name: "",
          type: "address",
          internalType: "contract LightAccount",
        },
      ],
      stateMutability: "view",
    },
    {
      type: "function",
      name: "ENTRY_POINT",
      inputs: [],
      outputs: [
        {
          name: "",
          type: "address",
          internalType: "contract IEntryPoint",
        },
      ],
      stateMutability: "view",
    },
    {
      type: "function",
      name: "acceptOwnership",
      inputs: [],
      outputs: [],
      stateMutability: "nonpayable",
    },
    {
      type: "function",
      name: "addStake",
      inputs: [
        { name: "unstakeDelay", type: "uint32", internalType: "uint32" },
        { name: "amount", type: "uint256", internalType: "uint256" },
      ],
      outputs: [],
      stateMutability: "payable",
    },
    {
      type: "function",
      name: "createAccount",
      inputs: [
        { name: "owner", type: "address", internalType: "address" },
        { name: "salt", type: "uint256", internalType: "uint256" },
      ],
      outputs: [
        {
          name: "account",
          type: "address",
          internalType: "contract LightAccount",
        },
      ],
      stateMutability: "nonpayable",
    },
    {
      type: "function",
      name: "getAddress",
      inputs: [
        { name: "owner", type: "address", internalType: "address" },
        { name: "salt", type: "uint256", internalType: "uint256" },
      ],
      outputs: [{ name: "", type: "address", internalType: "address" }],
      stateMutability: "view",
    },
    {
      type: "function",
      name: "owner",
      inputs: [],
      outputs: [{ name: "", type: "address", internalType: "address" }],
      stateMutability: "view",
    },
    {
      type: "function",
      name: "pendingOwner",
      inputs: [],
      outputs: [{ name: "", type: "address", internalType: "address" }],
      stateMutability: "view",
    },
    {
      type: "function",
      name: "renounceOwnership",
      inputs: [],
      outputs: [],
      stateMutability: "view",
    },
    {
      type: "function",
      name: "transferOwnership",
      inputs: [{ name: "newOwner", type: "address", internalType: "address" }],
      outputs: [],
      stateMutability: "nonpayable",
    },
    {
      type: "function",
      name: "unlockStake",
      inputs: [],
      outputs: [],
      stateMutability: "nonpayable",
    },
    {
      type: "function",
      name: "withdraw",
      inputs: [
        { name: "to", type: "address", internalType: "address payable" },
        { name: "token", type: "address", internalType: "address" },
        { name: "amount", type: "uint256", internalType: "uint256" },
      ],
      outputs: [],
      stateMutability: "nonpayable",
    },
    {
      type: "function",
      name: "withdrawStake",
      inputs: [{ name: "to", type: "address", internalType: "address payable" }],
      outputs: [],
      stateMutability: "nonpayable",
    },
    {
      type: "event",
      name: "OwnershipTransferStarted",
      inputs: [
        {
          name: "previousOwner",
          type: "address",
          indexed: true,
          internalType: "address",
        },
        {
          name: "newOwner",
          type: "address",
          indexed: true,
          internalType: "address",
        },
      ],
      anonymous: false,
    },
    {
      type: "event",
      name: "OwnershipTransferred",
      inputs: [
        {
          name: "previousOwner",
          type: "address",
          indexed: true,
          internalType: "address",
        },
        {
          name: "newOwner",
          type: "address",
          indexed: true,
          internalType: "address",
        },
      ],
      anonymous: false,
    },
    {
      type: "error",
      name: "AddressEmptyCode",
      inputs: [{ name: "target", type: "address", internalType: "address" }],
    },
    {
      type: "error",
      name: "AddressInsufficientBalance",
      inputs: [{ name: "account", type: "address", internalType: "address" }],
    },
    { type: "error", name: "FailedInnerCall", inputs: [] },
    { type: "error", name: "InvalidAction", inputs: [] },
    {
      type: "error",
      name: "InvalidEntryPoint",
      inputs: [{ name: "entryPoint", type: "address", internalType: "address" }],
    },
    {
      type: "error",
      name: "OwnableInvalidOwner",
      inputs: [{ name: "owner", type: "address", internalType: "address" }],
    },
    {
      type: "error",
      name: "OwnableUnauthorizedAccount",
      inputs: [{ name: "account", type: "address", internalType: "address" }],
    },
    {
      type: "error",
      name: "SafeERC20FailedOperation",
      inputs: [{ name: "token", type: "address", internalType: "address" }],
    },
    { type: "error", name: "TransferFailed", inputs: [] },
    { type: "error", name: "ZeroAddressNotAllowed", inputs: [] },
  ] as const;

export interface UserOperationContext {
    userOperation: any;
    hash: `0x${string}`;
    txHash: `0x${string}`;
    error: string;
    ids: string[];
}

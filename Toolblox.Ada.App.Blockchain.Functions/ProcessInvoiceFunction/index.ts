import { AzureFunction, Context } from "@azure/functions";
import { Edm, TableClient } from "@azure/data-tables";
const nearAPI = require("near-api-js");
const { keyStores, KeyPair, connect  } = nearAPI;
const myKeyStore = new keyStores.InMemoryKeyStore();
const PRIVATE_KEY = process.env.NearAccountPrivateKey;
// creates a public / private key pair using the provided private key
const keyPair = KeyPair.fromString(PRIVATE_KEY);
const connectionConfig = {
    networkId: "testnet",
    keyStore: myKeyStore, // first create a key store 
    nodeUrl: "https://rpc.testnet.near.org",
    walletUrl: "https://wallet.testnet.near.org",
    helperUrl: "https://helper.testnet.near.org",
    explorerUrl: "https://explorer.testnet.near.org",
  };

const tableStorageConnection = process.env["adawillhandlestorage_STORAGE"] || "";

const queueTrigger: AzureFunction = async function (context: Context, myQueueItem: string): Promise<void> {
  // adds the keyPair you created to keyStore 
  await myKeyStore.setKey("testnet", "accountant-ada.testnet", keyPair);
  const nearConnection = await connect(connectionConfig);
  const account = await nearConnection.account("accountant-ada.testnet");

  const client = TableClient.fromConnectionString(tableStorageConnection, `Invoices`);
  let accountantId = myQueueItem.split(':')[0].replace(/\"/gi, "");
  const invoice = await client.getEntity<Invoice>(accountantId, myQueueItem.split(':')[1]);

  try {
    const accountantClient = TableClient.fromConnectionString(tableStorageConnection, `Accountants`);
    const accountantList = accountantClient.listEntities<Accountant>({ 
      queryOptions: { 
        filter: `Contract eq '${accountantId}' or Workflow eq '${accountantId}'`, 
      }, 
    });
    let accountant : Accountant = undefined;
    for await (const accountantLine of accountantList) {
      accountant = accountantLine;
      break;
    }
    if (accountant == undefined)
    {
      throw new Error('Cannot find accountant with id ' + accountantId);
    }

    const contract = new nearAPI.Contract(account, accountant.Workflow, {
        viewMethods: ['getItem'],
        changeMethods: ['process', 'processExternal']
      });

    let processFee: number = accountant.ProcessFee;

    var alternativeCurrency = invoice.AlternativeCurrency;
    var alternativeFxValue = invoice.AlternativeFxValue;

    //todo get alternative currency
    if (!invoice.IsFiat && invoice.AlternativeFxValue == undefined)
    {
      if (invoice.Currency == "NEAR")
      {
        //get multiplier for wrap.testnet
        const oracleContract = new nearAPI.Contract(account, "priceoracle.testnet", {
          viewMethods: ['get_price_data'],
          changeMethods: []
        });
        var multiplier = (await oracleContract.get_price_data({ "asset_ids": ["wrap.testnet"] })).prices[0].price.multiplier;
        alternativeFxValue = (Number(multiplier.toString()) / 10000).toString();
        alternativeCurrency = "USD";
      }
    }
    
    if (invoice.ProcessedAt == undefined)
    {
      if (invoice.InvoiceNr == undefined)
      {
        console.log("Running processExternal");
        var item = await contract.processExternal({ "name" : invoice.Article, "amount" : invoice.Amount, "currency" : invoice.Currency, "from" : invoice.From, "to" : invoice.To, "receipt" : invoice.rowKey, "processFee" : processFee.toString() });
        var itemId = item.id;
        invoice.InvoiceNr = BigInt(itemId);
      }else{
        //process
        console.log("Running process for invoice " + invoice.InvoiceNr);
        await contract.process({ "id" : Number(invoice.InvoiceNr), "receipt": invoice.rowKey, "processFee" : processFee.toString() });
      }
    }

    invoice.ProcessedAt = new Date();
    invoice.ProcessFee = processFee;
    invoice.Error = '';
    invoice.AlternativeCurrency = alternativeCurrency;
    invoice.AlternativeFxValue = alternativeFxValue;
    await client.upsertEntity(invoice, "Merge");
    context.bindings.outQueueItem = myQueueItem;
  } catch (error) {
    const errorEntity = {
      partitionKey: invoice.partitionKey,
      rowKey: invoice.rowKey,
      Error: error.toString(),
    };
    await client.upsertEntity(errorEntity, "Merge");
    throw error;
  }
};

interface Invoice {
  partitionKey: string;
  rowKey: string;
  InvoiceNr? : bigint;
  CreatedAt: Date;
  From? : string;
  To? : string;
  Article? : string;
  ProcessedAt?: Date;
  Amount? : string;
  IsFiat?: boolean;
  ProcessFee?: number;
  Error? : string;
  Currency? : string;
  AlternativeCurrency? : string;
  AlternativeFxValue? : string;
}
interface Accountant {
  partitionKey: string;
  rowKey: string;
  ProcessFee: number;
  Workflow: string;
}
export default queueTrigger;

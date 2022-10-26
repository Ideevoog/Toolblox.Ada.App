import { AzureFunction, Context } from "@azure/functions";
import { TableClient } from "@azure/data-tables";
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

const tableName = `Invoices`;

const queueTrigger: AzureFunction = async function (context: Context, myQueueItem: string): Promise<void> {
    context.log('Using private key', PRIVATE_KEY);
    // adds the keyPair you created to keyStore 
    await myKeyStore.setKey("testnet", "silverdemo2.testnet", keyPair);
    const nearConnection = await connect(connectionConfig);
    const account = await nearConnection.account("silverdemo2.testnet");
    const contract = new nearAPI.Contract(account, 'invoice-workflow-123.testnet', {
        viewMethods: ['getItem'],
        changeMethods: ['process', 'processExternal']
      });

    const client = TableClient.fromConnectionString(tableStorageConnection, tableName);
    const invoice = await client.getEntity<Invoice>(myQueueItem.split(':')[0], myQueueItem.split(':')[1]);

    if (invoice.Id == undefined)
    {
      //processExternal
      var item = await contract.processExternal({ "id" : 1 });
    }else{
      //process
      await contract.process({ "id" : Number(invoice.Id), "receipt": invoice.rowKey, "processFee" : Number(1).toString() });
    }

    invoice.ProcessedAt = new Date();
    await client.upsertEntity(invoice, "Merge");

    context.bindings.outQueueItem = myQueueItem;
};

interface Invoice {
    partitionKey: string;
    rowKey: string;
    Id : bigint;
    CreatedAt: Date;
    ProcessedAt: Date;
  }

export default queueTrigger;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Toolblox.Ada.App.Model;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Toolblox.Ada.App.Functions
{
	public class BlockchainEventInvoice
	{
        /*         *
         * {"contract":"silver-test.testnet","from":"silverdemo2.testnet","to":"silverdemo2.testnet","article":"kekuleku"}
         */
        [JsonProperty("id")]
		public string Id { get; set; }
		[JsonProperty("contract")]
		public string Contract { get; set; }
		[JsonProperty("from")]
		public string From { get; set; }
		[JsonProperty("to")]
		public string To { get; set; }
        [JsonProperty("article")]
        public string Article { get; set; }
        [JsonProperty("amount")]
        public string Amount { get; set; }
        [JsonProperty("currency")]
        public string Currency { get; set; }
    }

	public class StoreEventFunction
    {
        [FunctionName("InvoiceFunction")]
        public async Task Run(
            [EventHubTrigger("invoiceevent", Connection = "EventHub")] EventData[] events,
            [Table("Invoices")] TableClient todoTable,
            ILogger log)
        {
            var exceptions = new List<Exception>();
            var actions = new List<TableTransactionAction>();

            foreach (EventData eventData in events)
            {
                try
                {
                    string messageBody = Encoding.UTF8.GetString(eventData.Body.Array, eventData.Body.Offset, eventData.Body.Count);
                    //var userId = await Security.GetUser(null);
                    var invoice = JsonConvert.DeserializeObject<BlockchainEventInvoice>(messageBody);
                    // Replace these two lines with your processing logic.
                    log.LogInformation($"C# Event Hub trigger function processed a message: {JsonConvert.SerializeObject(invoice, Formatting.Indented)}");
                    
                    actions.Add(new TableTransactionAction(
                        TableTransactionActionType.UpsertReplace,
                        new TableEntity(invoice.Contract, invoice.Id)
                        {
                            { "From", invoice.From },
                            { "To", invoice.To },
                            { "Article", invoice.Article },
                            { "Currency", invoice.Currency },
                            { "AmountString", invoice.Amount },
                            { "CreatedAt", DateTimeOffset.Now },
                            { "ModifiedAt", DateTimeOffset.Now }
                        }));
                }
                catch (Exception e)
                {
                    // We need to keep processing the rest of the batch - capture this exception and continue.
                    // Also, consider capturing details of the message that failed processing so it can be processed again later.
                    exceptions.Add(e);
                }
            }

            try
            {
                await todoTable.SubmitTransactionAsync(actions);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
            // Once processing of the batch is complete, if any messages in the batch failed processing throw an exception so that there is a record of the failure.

            if (exceptions.Count > 1)
                throw new AggregateException(exceptions);

            if (exceptions.Count == 1)
                throw exceptions.Single();
        }


        [FunctionName("UpsertAccountant")]
        public static async Task<IActionResult> UpsertAccountant(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Accountant/Upsert")] HttpRequestMessage req,
            [Table("Accountants")] TableClient todoTable,
            ILogger log)
        {
            dynamic body = await req.Content.ReadAsStringAsync();
            var accountant = JsonSerializer.Deserialize<Accountant>(body as string);
            //var userId = await Security.GetUser(req);
            string userId = "";
            if (string.IsNullOrWhiteSpace(accountant.User) || accountant.User != userId)
            {
                //check if not exist already
                var existingAccountant = await todoTable.QueryAsync<Azure.Data.Tables.TableEntity>(filter: $"PartitionKey eq 'testnet' and RowKey eq '{accountant.Id}'").FirstOrDefaultAsync();
                if (existingAccountant != null && existingAccountant.GetString("User") != userId)
                {
                    throw new Exception("Accountant already exists");
                }

            }
            var entity = new Azure.Data.Tables.TableEntity("testnet", accountant.Id.ToString())
            {
                { "User", userId },
                { "CreatedAt", DateTimeOffset.Now },
                { "ModifiedAt", DateTimeOffset.Now }
            };
            todoTable.UpsertEntity(entity);
            return new OkObjectResult("OK");
        }


        public static Accountant ToAccountant(Azure.Data.Tables.TableEntity tableEntity)
        {
            if (tableEntity == null)
            {
                return null;
            }
            return new Accountant
            {
                Id = Guid.TryParse(tableEntity.RowKey, out var id) ? id.ToString() : Guid.NewGuid().ToString(),
                User = tableEntity.GetString("User") ?? tableEntity.GetString("Owner"),
            };
        }


        public static Invoice ToInvoice(Azure.Data.Tables.TableEntity tableEntity)
        {
            if (tableEntity == null)
            {
                return null;
            }
            var invoice = new Invoice
            {
                Id = Guid.TryParse(tableEntity.RowKey, out var id) ? id.ToString() : Guid.NewGuid().ToString(),
                Contract = tableEntity.RowKey,
                From = tableEntity.GetString("From"),
                To = tableEntity.GetString("To"),
                Article = tableEntity.GetString("Article"),
                Currency = tableEntity.GetString("Currency"),
                AmountString = tableEntity.GetString("Amount"),
                Error = tableEntity.GetString("Error"),
                CreatedAt = tableEntity.GetDateTimeOffset("CreatedAt").GetValueOrDefault(),
                ModifiedAt = tableEntity.GetDateTimeOffset("ModifiedAt").GetValueOrDefault()
            };
            if (BigInteger.TryParse(invoice.AmountString, out var amount))
            {
                invoice.Amount = amount;
            }
            else
            {
                invoice.Error = "Cannot parse number from Amount";
            }
            return invoice;
        }
    }
}

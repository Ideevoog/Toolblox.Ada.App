using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using DinkToPdf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SendGrid.Helpers.Mail;
using Toolblox.Ada.App.Functions.Helpers;
using Toolblox.Ada.App.Functions.Services;
using Toolblox.Ada.App.Model;
using Toolblox.Ada.App.Model.Helpers;
using Toolblox.Ada.App.Model.Services;
using VCards;
using VCards.Types;

namespace Toolblox.Ada.App.Functions
{
	public class BlockchainEventInvoice
	{
		/*         *
         * {"contract":"silver-test.testnet","from":"silverdemo2.testnet","to":"silverdemo2.testnet","article":"kekuleku"}
         */
		[JsonProperty("id")]
		public long Id { get; set; }
		[JsonProperty("receiptId")]
		public string ReceiptId { get; set; }
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
		private readonly SynchronizedConverter _converter;

		public StoreEventFunction(SynchronizedConverter converter)
		{
			_converter = converter;
		}

		[FunctionName("Invoices")]
		public static async Task<IActionResult> Invoices(
			[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "Invoice/List")] HttpRequestMessage req,
			[Table("Accountants")] TableClient accountantsTable,
			[Table("Invoices")] TableClient todoTable,
			ILogger log)
		{
			log.LogInformation("C# HTTP trigger function processed a request.");
			var userId = await Security.GetUser(req, false);
			var query = req.RequestUri.ParseQueryString();
			var id = query.Get("id");

			var accountEntity = await accountantsTable.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{userId.Sanitize()}' and RowKey eq '{id.Sanitize()}'").FirstOrDefaultAsync();
			var accountant = accountEntity.ToAccountant();

			if (accountant == null)
			{
				throw new Exception("Cannot find accountant");
			}

			var invoiceEntities = await todoTable.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{accountant.Contract.Sanitize()}'").ToListAsync();

			var invoices = invoiceEntities.Select(TableEntityExtensions.ToInvoice).ToList();

			return new SystemTextJsonResult(invoices);
		}

		[FunctionName("Reprocess")]
		public static async Task<IActionResult> ReprocessInvoice(
			[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "Invoice/Reprocess")] HttpRequestMessage req,
			[Table("Accountants")] TableClient accountantsTable,
			[Table("Invoices")] TableClient invoiceTable,
			[Queue("invoices-to-automate")] ICollector<string> invoicesToAutomate,
			ILogger log)
		{
			log.LogInformation("C# HTTP trigger function processed a request.");
			var userId = await Security.GetUser(req, false);
			var query = req.RequestUri.ParseQueryString();
			var accountantId = query.Get("accountantId").Sanitize();
			var invoiceId = query.Get("id");
			var accountEntity = await accountantsTable.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{userId.Sanitize()}' and RowKey eq '{accountantId}'").FirstOrDefaultAsync();
			var accountant = accountEntity.ToAccountant();
			if (accountant == null)
			{
				throw new Exception("Cannot find accountant");
			}
			if (!accountant.IsActive)
			{
				throw new Exception("Accountant not deployed");
			}

			var partitionKey = invoiceId.Split(":")[0].Sanitize();
			var rowKey = invoiceId.Split(":")[1].Sanitize();

			var invoiceEntity = await invoiceTable.GetEntityAsync<TableEntity>(partitionKey, rowKey);

			if (invoiceEntity == null || (partitionKey != accountant.Contract && partitionKey != accountant.GetAddress()))
			{
				throw new Exception("Invoice not found");
			}

			invoicesToAutomate.Add(invoiceId);

			return new OkObjectResult("Started");
		}

		[FunctionName("AutomateInvoice")]
		public async Task AutomateInvoice(
			[QueueTrigger("invoices-to-automate")] string invoiceKey,
			[Table("Accountants")] TableClient accountantsTable,
			[Table("Invoices")] TableClient invoicesTable,
			[SendGrid(ApiKey = "CustomSendGridKeyAppSettingName")] IAsyncCollector<SendGridMessage> messageCollector,
			ILogger log)
		{
			if (string.IsNullOrWhiteSpace(invoiceKey) || invoiceKey.Split(":").Length != 2)
			{
				//Ignore invalid item
				return;
			}

			var adaContract = invoiceKey.Split(":")[0];
			var receipt = invoiceKey.Split(":")[1];
			var invoiceEntity = await invoicesTable.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{adaContract.Sanitize()}' and RowKey eq '{receipt.Sanitize()}'").FirstOrDefaultAsync();
			if (invoiceEntity == null)
			{
				throw new Exception("Invoice not found: " + invoiceKey);
			}

			try
			{
				var invoice = invoiceEntity.ToInvoice();
				var accountantEntity = await accountantsTable.QueryAsync<TableEntity>(filter: $"Contract eq '{adaContract.Sanitize()}'").ToListAsync();
				if (!accountantEntity.Any())
				{
					throw new Exception("No accountants found to process invoice: " + invoiceKey);
				}
				foreach (var accountant in accountantEntity.Select(TableEntityExtensions.ToAccountant))
				{
					var client = new SecretClient(vaultUri: new Uri("https://adaaddressbookkeys.vault.azure.net/"), credential: new DefaultAzureCredential());
					var keys = KeysSerializer.Deserialize((await client.GetSecretAsync($"ada{accountant.Id}".Replace("-", "")))?.Value?.Value);
					if (keys == null)
					{
						throw new Exception("Ada has no access to address book of accountant " + accountant.Id);
					}

					var service = new EncryptionServiceClient(null);
					var masterKey = await service.GetAddressBookMasterKey(accountant, keys);
					var addressBook = await service.GetAddressBook(accountant, masterKey);

					var contacts = addressBook.ToLookup(accountant);

					byte[] pdf = null;
					var seller = contacts.FindContact(invoice.From);
					var buyer = contacts.FindContact(invoice.To, false);
					if (accountant.Tasks.Any())
					{
						pdf = BuildPdf(invoice, seller, buyer);
					}
					foreach (var task in accountant.Tasks.OfType<SendEmailAccountingTask>())
					{
						var message = new SendGridMessage();
						switch (task.Type)
						{
							case SendEmailTaskType.Buyer:
								message.AddTo(buyer.GetEmail()?.EmailAddress);
								break;
							case SendEmailTaskType.Seller:
								message.AddTo(seller.GetEmail()?.EmailAddress);
								break;
							case SendEmailTaskType.Custom:
								message.AddTo(task.Email);
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}

						var alternativeAmountString = "";
						if (invoice.HasAlternativeAmount())
						{
							alternativeAmountString = $"$ {invoice.GetAmountInAlternativeCurrency():0.00}<br/>";
						}
						message.AddContent("text/html", $"<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">\r\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\r\n <head>\r\n  <meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\" />\r\n  <title>Invoice details</title>\r\n  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"/>\r\n </head>\r\n  \r\n  <body style=\"margin: 0; padding: 0; background-color:#eaeced \" bgcolor=\"#eaeced\">\r\n   <table bgcolor=\"#eaeced\" cellpadding=\"0\" cellspacing=\"0\" width=\"100%\" style=\"background-color: #eaeced; \">\r\n     <tr>\r\n     \t<td>&nbsp;</td>\r\n     </tr>\r\n     <tr>\r\n     \t<td>&nbsp;</td>\r\n     </tr>\r\n    <tr>\r\n     <td>\r\n      \r\n       <table align=\"center\" bgcolor=\"#ffffff\" cellpadding=\"20\" cellspacing=\"0\" width=\"600\" \r\n              style=\"border-collapse: collapse; background-color: #ffffff; border: 1px solid #f0f0f0;\">\r\n         <tr style=\"border-top: 4px solid #ff0000;\">\r\n          <td align=\"left\" style=\"padding: 15px 20px 20px;\">\r\n            <table width=\"100%\">\r\n              <tr>\r\n                <td><img style=\"width: 200px;\" src='{accountant.Logo}' width=\"220px\" alt=\"Company Logo\"/></td>\r\n                <td align=\"right\" style=\"font-family: 'Open Sans',Helvetica,Arial,sans-serif;\">\r\n                  <span>Inovice no: #{invoice.InvoiceNr.GetValueOrDefault().ToString("0000")}</span><br>\r\n                  <span style=\"padding: 5px 0; display: block;\">{invoice.CreatedAt.DateTime.ToShortDateString()}</span>\r\n                </td>\r\n              </tr>\r\n            </table>\r\n           \r\n          </td>\r\n         </tr>\r\n         <tr>\r\n          <td align=\"center\" style=\"padding: 20px; border-top: 1px solid #f0f0f0; background: #fafafa; font-family: 'Open Sans',Helvetica,Arial,sans-serif; \">\r\n           <div>Total Due:</div>\r\n           <h2 style=\"margin: 10px 0; color: #333; font-weight: 500; font-size: 48px;\">\r\n              {invoice.GetAmount():0.00} {invoice.Currency}\r\n           </h2>\r\n            <div style=\"line-height: 1.4; font-size: 1.2; font-size: 14px; color: #777;\">{alternativeAmountString}{(buyer?.Kind == Kind.Organization ? $"For {buyer.Organization}, " : "")}Issued on {invoice.CreatedAt.DateTime.ToLongDateString()}<br>by {seller.Organization}</div>\r\n          </td>\r\n         </tr>\r\n         <tr>\r\n          <td align=\"center\" style=\"padding: 20px 40px; font-family: 'Open Sans',Helvetica,Arial,sans-serif;font-size: 16px;line-height: 1.4;color: #333;\">\r\n            <div>Note: For {invoice.Article}</div>\r\n            <div><br></div>\r\n            <div><br></div>\r\n          </td>\r\n         </tr>\r\n         <tr style=\"border-top: 1px solid #eaeaea;\">\r\n           <td align=\"center\">\r\n             <div style=\"font-family: 'Open Sans',Helvetica,Arial,sans-serif;font-size: 14px;line-height: 1.4;color: #777;\">\r\n              Regards,<br>\r\n              {seller.Organization}\r\n            </div>\r\n           </td>\r\n         </tr>\r\n       </table>\r\n       \r\n     </td>\r\n    </tr>\r\n     <tr>\r\n     \t<td>&nbsp;</td>\r\n     </tr>\r\n     <tr>\r\n     \t<td>&nbsp;</td>\r\n     </tr>\r\n   </table>\r\n  </body>\r\n  \r\n</html>");
						if (pdf != null)
						{
							message.AddAttachment("invoice.pdf", Convert.ToBase64String(pdf));
						}
						message.SetFrom(new EmailAddress("ada@toolblox.net", accountant.Name));
						message.SetSubject("Invoice from " + accountant.Name);

						await messageCollector.AddAsync(message);
					}

					invoiceEntity["AutomationFinishedAt"] = DateTimeOffset.Now;
					invoiceEntity["Error"] = "";
					await invoicesTable.UpsertEntityAsync(invoiceEntity);
				}
			}
			catch (Exception e)
			{
				invoiceEntity["Error"] = e.Message;
				await invoicesTable.UpsertEntityAsync(invoiceEntity);
				log.LogError(e, "Error automating");
				throw;
			}
		}

		private byte[] BuildPdf(Invoice invoice, VCard seller, VCard buyer)
		{
			var htmlContent = invoice.GetInvoiceAsHtml(seller, buyer);
			var pdf = BuildPdf(htmlContent, "8.5in", "11in", new MarginSettings(0, 0, 0, 0));
			return pdf;
		}

		private byte[] BuildPdf(string HtmlContent, string Width, string Height, MarginSettings Margins, int? DPI = 180)
		{
			// Call the Convert method of SynchronizedConverter "pdfConverter"
			return _converter.Convert(new HtmlToPdfDocument()
			{
				// Set the html content
				Objects =
				{
					new ObjectSettings
					{
						HtmlContent = HtmlContent
					}
				},
				// Set the configurations
				GlobalSettings = new GlobalSettings
				{
					// PaperKind.A4 can also be used instead PechkinPaperSize
					PaperSize = new PechkinPaperSize(Width, Height),
					DPI = DPI,
					Margins = Margins
				}
			});
		}

#if !DEBUG
        [FunctionName("InvoiceFunction")]
#endif
		public async Task Run(
			[EventHubTrigger("invoiceevent", Connection = "EventHub")] EventData[] events,
			[Table("Invoices")] TableClient todoTable,
			[Queue("invoices-to-process")] ICollector<string> invoicesToProcess,
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

					invoicesToProcess.Add($"{invoice.Contract}:{invoice.ReceiptId}");
					var currencyIso = invoice.Currency?.ToUpper();
					actions.Add(new TableTransactionAction(
						TableTransactionActionType.UpsertReplace,
						new TableEntity(invoice.Contract, invoice.ReceiptId)
						{
							{ "InvoiceNr", invoice.Id },
							{ "From", invoice.From },
							{ "To", invoice.To },
							{ "Article", invoice.Article },
							{ "IsFiat", Invoice.CheckIfFiat(currencyIso) },
							{ "Currency", currencyIso },
							{ "AmountString", invoice.Amount },
							{ "Amount", invoice.Amount },
							{ "CreatedAt", DateTimeOffset.Now },
							{ "ProcessedAt", (DateTimeOffset?)null },
							{ "AutomationFinishedAt", (DateTimeOffset?)null },
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
	}
}

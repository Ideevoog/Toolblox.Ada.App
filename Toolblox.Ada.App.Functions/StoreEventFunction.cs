using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using DinkToPdf;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SendGrid.Helpers.Mail;
using Toolblox.Ada.App.Functions.Helpers;
using Toolblox.Ada.App.Model;
using Toolblox.Ada.App.Model.Helpers;
using Toolblox.Ada.App.Model.Services;
using Toolblox.Model;
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

		[FunctionName("AutomateInvoice")]
	    public async Task AutomateInvoice(
		    [QueueTrigger("invoices-to-automate")] string invoiceKey,
		    [Table("Accountants")] TableClient accountantsTable,
		    [Table("Invoices")] TableClient invoicesTable,
		    [SendGrid(ApiKey = "CustomSendGridKeyAppSettingName")] IAsyncCollector<SendGridMessage> messageCollector)
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
				var contactInfo = Deserializer.GetVCard(accountant.ContactInfo);

				var contacts = new []{ contactInfo }
					.Concat(addressBook)
					.SelectMany(x => x.GetWalletData()
						.Where(w => w.Chain == Blockchain.Near && w.Kind == BlockchainKind.Testnet)
						.SelectMany(w => (w.Contracts ?? string.Empty).Split(",").Select(c => c.Trim())
						.Where(c => !string.IsNullOrWhiteSpace(c))
						.Select(c => new { Address = c, Details = x }))
						.Concat(new [] { new { Address = accountant.Contract, Details = contactInfo } }))
					.ToLookup(x => x.Address);
				VCard FindContact(string address)
				{
					var contact = contacts[address].FirstOrDefault()?.Details;
					if (contact == null)
					{
						throw new Exception($"Cannot find contact " + address);
					}
					return contact;
				};
				byte[] pdf = null;
				var seller = FindContact(invoice.From);
				var buyer = FindContact(invoice.To);
				bool hasOrgBuyer = buyer?.Kind == Kind.Organization;
				if (accountant.Tasks.Any())
				{
					var amountWithoutVat = invoice.GetAmountWithoutVat(contactInfo);
					var amountWithVat = invoice.GetAmountWithVat();
					var hasVat = contactInfo.HasVat();
					pdf = BuildPdf($"\r\n<!DOCTYPE HTML> \r\n<html>\r\n<head>\r\n<meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\">\r\n<style>\r\n*\r\n{{\r\n\tmargin: 0;\r\n\tpadding: 0;\r\n}}\r\n\r\nhtml, body\r\n{{\r\n\twidth: 100%;\r\n\theight: 100%;\r\n\tfont: normal 14px Arial;\r\n\tbackground-color: #efefef;\r\n}}\r\n\r\nh1\r\n{{\r\n\tfont-size: 24px;\r\n}}\r\n\r\nh2\r\n{{\r\n\tfont-size: 18px;\r\n\tmargin: 10px 0px 10px 0px;\r\n}}\r\n\r\ninput\r\n{{\r\n\toutline: none;\r\n\tcursor: pointer;\r\n\tfont: normal 14px Arial;\r\n\tborder: 0px solid #fff; /* fixes the ie problem */\r\n}}\r\n\r\na:link, a:visited\r\n{{\r\n\tcolor: #0000ee;\r\n\ttext-decoration: none;\r\n}}\r\n\r\na:hover, a:active\r\n{{\r\n\tcolor: #0000ee;\r\n\ttext-decoration: underline;\r\n}}\r\n\r\n#toolbar\r\n{{\r\n\ttop: 0px;\r\n\tleft: 0px;\r\n\twidth: 100%;\r\n\tcolor: #444;\r\n\tdisplay: none;\r\n\tposition: absolute;\r\n\tfont: normal 11px Verdana;\r\n\tbackground-color: #ffffcc;\r\n\tborder-bottom: 1px solid #c9c9c9;\r\n}}\r\n\r\n#toolbar div\r\n{{\r\n\twidth: 850px;\r\n\tmargin: 0 auto;\r\n\ttext-align: left;\r\n\tpadding: 15px 0px;\r\n}}\r\n\r\n#container\r\n{{\r\n    min-height: 100%;\r\n    height: auto !important;\r\n    height: 100%;\r\n\r\n\twidth: 850px;\r\n\tmargin: 0 auto;\r\n\tpadding: 0px 15px;\r\n\tline-height: 20px;\r\n\tbackground-color: #fff;\r\n}}\r\n\r\n#header\r\n{{\r\n\theight: 40px;\r\n\ttext-align: left;\r\n\tpadding: 20px 0px 30px 0px;\r\n}}\r\n\r\n#footer\r\n{{\r\n\tpadding: 30px 0px;\r\n}}\r\n\r\n#footer input\r\n{{\r\n\twidth: 200px;\r\n}}\r\n\r\n.mouseFocus, .keyboardFocus\r\n{{\r\n\tbackground-color: #ffffcc;\r\n}}\r\n\r\n\r\n/**\r\n * \r\n * div: #invoice-info\r\n * \r\n**/\r\n\r\n#invoice-info\r\n{{\r\n\tpadding: 10px 0px 25px 0px;\r\n}}\r\n\r\n#invoice-info .title\r\n{{\r\n\tcolor: #333;\r\n\twidth: 120px;\r\n\tfont-weight: bold;\r\n\tvertical-align: top;\r\n}}\r\n\r\n#invoice-info .value input\r\n{{\r\n\tcolor: #555;\r\n\twidth: 270px;\r\n}}\r\n\r\n#my-company\r\n{{\r\n\tfloat: right;\r\n\twidth: 400px;\r\n}}\r\n\r\n#buyer-company\r\n{{\r\n\twidth: 400px;\r\n}}\r\n\r\n\r\n/**\r\n * \r\n * div: #invoice-content\r\n * \r\n**/\r\n#invoice-content input, #invoice-summary input\r\n{{\r\n\twidth: 100%;\r\n}}\r\n\r\n#invoice-content table\r\n{{\r\n\twidth: 100%;\r\n\ttext-align: left;\r\n\tborder-collapse: collapse;\r\n}}\r\n\r\n#invoice-content th\r\n{{\r\n\tpadding: 5px;\r\n\tcursor: pointer;\r\n\tborder: 1px solid #ccc;\r\n\tbackground-color: #f0f0f0;\r\n}}\r\n\r\n#invoice-content td\r\n{{\r\n\theight: 20px;\r\n\tpadding: 5px;\r\n\tborder: 1px solid #ccc;\r\n}}\r\n\r\n#invoice-content .white-border td\r\n{{\r\n\tborder: none;\r\n}}\r\n\r\n.row-price input, .row-amount input, .row-sum\r\n{{\r\n\ttext-align: right;\r\n}}\r\n\r\n#invoice-summary .title\r\n{{\r\n\ttext-align: right;\r\n\tfont-weight: bold;\r\n}}\r\n\r\n#invoice-summary .value, #invoice-currency\r\n{{\r\n\ttext-align: right;\r\n}}\r\n\r\n</style>\r\n<title>Idera</title>\r\n</head>\r\n<body>\r\n<div id=\"container\">\r\n<div id=\"header\">\r\n<h1></h1>\r\n</div>\r\n<div id=\"invoice-info\">\r\n<table id=\"my-company\">\r\n<tr>\r\n<td colspan=\"2\"><h2>Seller</h2></td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">Name:</td>\r\n<td class=\"value\"><input id=\"my-company-name\" name=\"my-company-name\" class=\"save\" type=\"text\" value=\"{contactInfo.Organization}\"></td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">Aaddress:</td>\r\n<td class=\"value\"><input id=\"my-company-address\" name=\"my-company-address\" class=\"save\" type=\"text\" value=\"{contactInfo.GetAddress().ToSummary()}\"></td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">Reg. nr:</td>\r\n<td class=\"value\"><input id=\"my-company-regnr\" name=\"my-company-regnr\" class=\"save\" type=\"text\" value=\"{contactInfo.GetExtension(VCardAdaConstants.OrgNr)}\"></td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">{(hasVat ? "KMKR nr:" : "")}</td>\r\n<td class=\"value\"><input id=\"my-company-vatnr\" name=\"my-company-vatnr\" class=\"save\" type=\"text\" value=\"{(hasVat ? contactInfo.GetExtension(VCardAdaConstants.VatNr) : "")}\"></td>\r\n</tr>\r\n<tr><td colspan=\"2\">&nbsp;</td></tr>\r\n<tr>\r\n<td class=\"title\">Account:</td>\r\n<td class=\"value\"><input id=\"my-company-banknr\" name=\"my-company-banknr\" class=\"save\" type=\"text\" value=\"{contactInfo.GetBankData().FirstOrDefault()?.GetSummary()}\"></td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">Telephone:</td>\r\n<td class=\"value\"><input id=\"my-company-phone\" name=\"my-company-phone\" class=\"save\" type=\"text\" value=\"{contactInfo.GetPhone()?.Number}\"></td>\r\n</tr>\r\n</table>\r\n<table id=\"buyer-company\">\r\n<tr>\r\n<td colspan=\"2\"><h2>Invoice</h2></td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">Invoice nr:</td>\r\n<td class=\"value\"><input id=\"invoice-nr\" name=\"invoice-nr\" type=\"text\" value=\"{invoice.InvoiceNr.GetValueOrDefault().ToString("0000")}\"></td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">Issued:</td>\r\n<td class=\"value\"><input id=\"invoice-date\" name=\"invoice-date\" type=\"text\" value=\"{invoice.CreatedAt.DateTime.ToShortDateString()}\"></td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">Due date:</td>\r\n<td class=\"value\"><input id=\"invoice-due-date\" name=\"invoice-due-date\" type=\"text\" value=\"{invoice.CreatedAt.AddDays(30).DateTime.ToShortDateString()}\"></td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">Overdue charge:</td>\r\n<td class=\"value\"><input id=\"invoice-overdue-charge\" name=\"invoice-overdue-charge\" class=\"save\" type=\"text\" value=\"0,1% / day\"></td>\r\n</tr>\r\n<tr><td colspan=\"2\">&nbsp;</td></tr>\r\n<tr>\r\n<td colspan=\"2\">{(hasOrgBuyer ? "<h2>Buyer</h2>" : "")}</td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">{(hasOrgBuyer ? "Nimi:" : "")}</td>\r\n<td class=\"value\"><input id=\"buyer-name\" name=\"buyer-name\" type=\"text\" value=\"{(hasOrgBuyer ? buyer.Organization : "")}\"></td>\r\n</tr>\r\n<tr>\r\n<td class=\"title\">{(hasOrgBuyer ? "Aadress:" : "")}</td>\r\n<td class=\"value\"><input id=\"buyer-address\" name=\"buyer-address\" type=\"text\" value=\"{(hasOrgBuyer ? buyer.GetAddress().ToSummary() : "")}\"></td>\r\n</tr>\r\n</table>\r\n</div>\r\n\r\n<div id=\"invoice-content\">\r\n<table>\r\n<thead>\r\n<tr>\r\n<th style=\"text-align: left;\">Article</th>\r\n<th style=\"width: 100px; text-align: right;\">Quantity</th>\r\n<th style=\"width: 100px; text-align: right;\">Price</th>\r\n<th style=\"width: 100px; text-align: right;\">Total</th>\r\n</tr>\r\n</thead>\r\n<tbody>\r\n<tr>\r\n<td class=\"row-name\"><input type=\"text\" value=\"{invoice.Article}\" ></td>\r\n<td class=\"row-amount\"><input type=\"text\" value=\"1\"></td>\r\n<td class=\"row-price\"><input type=\"text\" value=\"{amountWithVat}\"></td>\r\n<td class=\"row-sum\"><input type=\"text\" value=\"{amountWithVat}\"></td>\r\n</tr>\r\n</tbody>\r\n<tfoot id=\"invoice-summary\">\r\n<tr class=\"white-border\">\r\n<td colspan=\"4\">&nbsp;</td>\r\n</tr>\r\n<tr class=\"white-border\">\r\n<td colspan=\"3\" class=\"title\">Sum excl. VAT:</td>\r\n<td id=\"sum-without-vat\" class=\"value\">{amountWithoutVat}</td>\r\n</tr>\r\n<tr class=\"white-border\">\r\n<td colspan=\"3\" class=\"title\">VAT {contactInfo.GetExtension(VCardAdaConstants.VatPercentage)}%:</td>\r\n<td id=\"sum-vat\" class=\"value\">{amountWithVat}</td>\r\n</tr>\r\n<tr class=\"white-border\">\r\n<td colspan=\"3\" class=\"title\">Sum:</td>\r\n<td id=\"sum\" class=\"value\">{amountWithVat}</td>\r\n</tr>\r\n<tr class=\"white-border\">\r\n<td colspan=\"3\" class=\"title\">Currency:</td>\r\n<td id=\"currency\" class=\"value\"><input id=\"invoice-currency\" name=\"invoice-currency\" type=\"text\" class=\"save\" value=\"{invoice.Currency}\"></td>\r\n</tr>\r\n</tfoot>\r\n</table>\r\n</div>\r\n<div id=\"footer\">\r\nIssuer <input id=\"my-company-person\" name=\"my-company-person\" type=\"text\" class=\"save\" value=\"{contactInfo.Organization}\">\r\n<br>\r\n<br>\r\nContact: <input id=\"my-company-email\" name=\"my-company-email\" class=\"save\" type=\"text\" value=\"{contactInfo.GetContactSummary()}\">\r\n</div>\r\n</div>\r\n</body>\r\n</html>\r\n", "8.5in", "11in", new MarginSettings(0, 0, 0, 0));
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
					message.AddContent("text/html", $"<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">\r\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\r\n <head>\r\n  <meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\" />\r\n  <title>Invoice details</title>\r\n  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"/>\r\n </head>\r\n  \r\n  <body style=\"margin: 0; padding: 0; background-color:#eaeced \" bgcolor=\"#eaeced\">\r\n   <table bgcolor=\"#eaeced\" cellpadding=\"0\" cellspacing=\"0\" width=\"100%\" style=\"background-color: #eaeced; \">\r\n     <tr>\r\n     \t<td>&nbsp;</td>\r\n     </tr>\r\n     <tr>\r\n     \t<td>&nbsp;</td>\r\n     </tr>\r\n    <tr>\r\n     <td>\r\n      \r\n       <table align=\"center\" bgcolor=\"#ffffff\" cellpadding=\"20\" cellspacing=\"0\" width=\"600\" \r\n              style=\"border-collapse: collapse; background-color: #ffffff; border: 1px solid #f0f0f0;\">\r\n         <tr style=\"border-top: 4px solid #ff0000;\">\r\n          <td align=\"left\" style=\"padding: 15px 20px 20px;\">\r\n            <table width=\"100%\">\r\n              <tr>\r\n                <td><img style=\"width: 200px;\" src='{accountant.Logo}' width=\"220px\" alt=\"Company Logo\"/></td>\r\n                <td align=\"right\" style=\"font-family: 'Open Sans',Helvetica,Arial,sans-serif;\">\r\n                  <span>Inovice no: #{invoice.InvoiceNr.GetValueOrDefault().ToString("0000")}</span><br>\r\n                  <span style=\"padding: 5px 0; display: block;\">{invoice.CreatedAt.DateTime.ToShortDateString()}</span>\r\n                </td>\r\n              </tr>\r\n            </table>\r\n           \r\n          </td>\r\n         </tr>\r\n         <tr>\r\n          <td align=\"center\" style=\"padding: 20px; border-top: 1px solid #f0f0f0; background: #fafafa; font-family: 'Open Sans',Helvetica,Arial,sans-serif; \">\r\n           <div>Total Due:</div>\r\n           <h2 style=\"margin: 10px 0; color: #333; font-weight: 500; font-size: 48px;\">\r\n              {invoice.Amount} {invoice.Currency}\r\n           </h2>\r\n            <div style=\"line-height: 1.4; font-size: 1.2; font-size: 14px; color: #777;\">{(buyer?.Kind == Kind.Organization ? $"For {buyer.Organization}, " : "")}Issued on {invoice.CreatedAt.DateTime.ToLongDateString()}<br>by {contactInfo.Organization}</div>\r\n          </td>\r\n         </tr>\r\n         <tr>\r\n          <td align=\"center\" style=\"padding: 20px 40px; font-family: 'Open Sans',Helvetica,Arial,sans-serif;font-size: 16px;line-height: 1.4;color: #333;\">\r\n            <div>Note: For {invoice.Article}</div>\r\n            <div><br></div>\r\n            <div><br></div>\r\n          </td>\r\n         </tr>\r\n         <tr style=\"border-top: 1px solid #eaeaea;\">\r\n           <td align=\"center\">\r\n             <div style=\"font-family: 'Open Sans',Helvetica,Arial,sans-serif;font-size: 14px;line-height: 1.4;color: #777;\">\r\n              Regards,<br>\r\n              {contactInfo.Organization}\r\n            </div>\r\n           </td>\r\n         </tr>\r\n       </table>\r\n       \r\n     </td>\r\n    </tr>\r\n     <tr>\r\n     \t<td>&nbsp;</td>\r\n     </tr>\r\n     <tr>\r\n     \t<td>&nbsp;</td>\r\n     </tr>\r\n   </table>\r\n  </body>\r\n  \r\n</html>");
					if (pdf != null)
					{
						message.AddAttachment("invoice.pdf", Convert.ToBase64String(pdf));
					}
					message.SetFrom(new EmailAddress("ada@toolblox.net", accountant.Name));
					message.SetSubject("Invoice from " + accountant.Name);
					
					await messageCollector.AddAsync(message);
				}
		    }
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

                    actions.Add(new TableTransactionAction(
                        TableTransactionActionType.UpsertReplace,
                        new TableEntity(invoice.Contract, invoice.ReceiptId)
						{
							{ "InvoiceNr", invoice.Id },
							{ "From", invoice.From },
							{ "To", invoice.To },
                            { "Article", invoice.Article },
                            { "Currency", invoice.Currency?.ToUpper() },
                            { "AmountString", invoice.Amount },
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

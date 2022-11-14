using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Toolblox.Ada.App.Model;
using Toolblox.Model;

namespace Toolblox.Ada.App.Functions.Helpers
{
	public static class TableEntityExtensions
	{
		public static Accountant ToAccountant(this TableEntity tableEntity)
		{
			if (tableEntity == null)
			{
				return null;
			}
			var serializerOptions = new JsonSerializerOptions().ConfigureAdaDtoInheritance();
			var tasks = tableEntity.GetString("Tasks");
			var addressBookAccessRights = tableEntity.GetString("AddressBookAccessRights");
			return new Accountant
			{
				Name = tableEntity.GetString("Name"),
				NearMainnet = tableEntity.GetString("NearMainnet"),
				Logo = tableEntity.GetString("Logo"),
				AddressBookUrl = tableEntity.GetString("AddressBookUrl"),
				ContactInfo = tableEntity.GetString("ContactInfo"),
				PublicKey = tableEntity.GetString("PublicKey"),
				NearTestnet = tableEntity.GetString("NearTestnet"),
				SelectedBlockchainKind = (BlockchainKind)tableEntity.GetInt32("SelectedBlockchainKind").GetValueOrDefault(),
				SelectedChain = (Blockchain)tableEntity.GetInt32("SelectedChain").GetValueOrDefault(),
				Id = Guid.TryParse(tableEntity.RowKey, out var id) ? id.ToString() : Guid.NewGuid().ToString(),
				User = tableEntity.PartitionKey,
				CreatedAt = tableEntity.GetDateTimeOffset("CreatedAt").GetValueOrDefault(),
				ModifiedAt = tableEntity.GetDateTimeOffset("ModifiedAt").GetValueOrDefault(),
				IsDeployed = tableEntity.GetBoolean("IsDeployed") ?? false,
				IsActive = tableEntity.GetBoolean("IsActive") ?? false,
				DeployedAt = tableEntity.GetDateTimeOffset("DeployedAt").GetValueOrDefault(),
				ActivatedAt = tableEntity.GetDateTimeOffset("ActivatedAt").GetValueOrDefault(),
				EditStep = (AccountantEditStep)tableEntity.GetInt32("EditStep"),
				Tasks = tasks == null
					? new List<AccountingTaskBase>()
					: JsonSerializer.Deserialize<List<AccountingTaskBase>>(tasks, serializerOptions)!,
				AddressBookAccessRights = addressBookAccessRights == null
					? new List<ContentAccessRight>()
					: JsonSerializer.Deserialize<List<ContentAccessRight>>(addressBookAccessRights, serializerOptions)!
			};
		}

		public static Invoice ToInvoice(this TableEntity tableEntity)
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
				InvoiceNr = tableEntity.GetInt64("InvoiceNr"),
				Article = tableEntity.GetString("Article"),
				Currency = tableEntity.GetString("Currency"),
				AmountString = tableEntity.GetString("Amount"),
				Error = tableEntity.GetString("Error"),
				CreatedAt = tableEntity.GetDateTimeOffset("CreatedAt").GetValueOrDefault(),
				ProcessedAt = tableEntity.GetDateTimeOffset("ModifiedAt"),
				AutomationFinishedAt = tableEntity.GetDateTimeOffset("ModifiedAt"),
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

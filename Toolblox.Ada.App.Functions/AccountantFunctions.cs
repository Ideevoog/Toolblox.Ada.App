using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Amqp.Serialization;
using Toolblox.Ada.App.Functions.Helpers;
using Toolblox.Ada.App.Functions.Services;
using Toolblox.Ada.App.Model;
using Toolblox.Model;

namespace Toolblox.Ada.App.Functions
{
    public class AccountantFunctions
    {
	    [FunctionName("Accountants")]
        public static async Task<IActionResult> Accountants(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "Accountant/List")] HttpRequestMessage req,
            [Table("Accountants")] TableClient todoTable,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            var userId = await Security.GetUser(req, false);
            var filter = !string.IsNullOrWhiteSpace(userId)
                ? $"PartitionKey eq '{userId.Sanitize()}' or (IsPublic and IsDeployed)"
                : $"IsPublic and IsDeployed";

            var pages = await todoTable.QueryAsync<TableEntity>(filter: filter).AsPages().ToListAsync();
            var workflows = pages.SelectMany(x => x.Values).Select(ToAccountant).ToList();

            return new SystemTextJsonResult(workflows);
        }

        [FunctionName("UpsertAccountant")]
        public static async Task<IActionResult> UpsertAccountant(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Accountant/Upsert")] HttpRequestMessage req,
            [Table("Accountants")] TableClient todoTable,
            ILogger log)
        {
            dynamic body = await req.Content.ReadAsStringAsync();
            var serializerOptions = new JsonSerializerOptions().ConfigureAdaDtoInheritance();
            var accountant = JsonSerializer.Deserialize<Accountant>(body as string, serializerOptions);
            var userId = await Security.GetUser(req);
            if (string.IsNullOrWhiteSpace(accountant.User) || accountant.User != userId)
            {
                //check if not exist already
                var existingAccountant = await todoTable.QueryAsync<TableEntity>(filter: $"RowKey eq '{accountant.Id.Sanitize()}'").FirstOrDefaultAsync();
                if (existingAccountant != null && existingAccountant.PartitionKey != userId)
                {
                    throw new Exception("Accountant already exists");
                }
            }
            var entity = new TableEntity(userId, accountant.Id.ToString())
			{
				{ "IsDeployed", accountant.IsDeployed },
				{ "IsActive", accountant.IsActive },
				{ "PublicKey", accountant.PublicKey },
				{ "NearTestnet", accountant.NearTestnet },
				{ "NearMainnet", accountant.NearMainnet },
				{ "Name", accountant.Name },
				{ "SelectedBlockchainKind", (int)accountant.SelectedBlockchainKind },
				{ "SelectedChain", (int)accountant.SelectedChain },
				{ "EditStep", (int)accountant.EditStep },
				{ "CreatedAt", accountant.CreatedAt != DateTimeOffset.MinValue ? accountant.CreatedAt : DateTimeOffset.Now },
				{ "ActivatedAt", accountant.ActivatedAt.HasValue && accountant.ActivatedAt != DateTimeOffset.MinValue ? accountant.ActivatedAt : null },
				{ "ModifiedAt", DateTimeOffset.Now },
				{ "DeployedAt", accountant.DeployedAt },
				{ "ContactInfo", accountant.ContactInfo },
				{ "Tasks", JsonSerializer.Serialize(accountant.Tasks, serializerOptions) },
			};
            todoTable.UpsertEntity(entity);
            return new OkObjectResult("OK");
        }

        [FunctionName("DeleteAccountant")]
        public static async Task<IActionResult> DeleteAccountant(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Accountant/Delete")] HttpRequestMessage req,
            [Table("Accountants")] TableClient todoTable,
            ILogger log)
        {
            var query = req.RequestUri.ParseQueryString();
            var id = query.Get("id");
            var userId = await Security.GetUser(req);
            var tableEntity = await todoTable.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{userId.Sanitize()}' and RowKey eq '{id.Sanitize()}'").FirstOrDefaultAsync();
            if (tableEntity == null)
            {
                return new NotFoundObjectResult(null);
            }
            await todoTable.DeleteEntityAsync(tableEntity.PartitionKey, tableEntity.RowKey);
            return new OkObjectResult("OK");
        }

        [FunctionName("AccountantById")]
        public static async Task<IActionResult> AccountantById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "Accountant/GetById")] HttpRequestMessage req,
            [Table("Accountants")] TableClient todoTable,
            ILogger log)
        {
            var query = req.RequestUri.ParseQueryString();
            var id = query.Get("id");
            var tableEntity = await todoTable.QueryAsync<TableEntity>(filter: $"RowKey eq '{id.Sanitize()}'").FirstOrDefaultAsync();
            var workflowMetadata = ToAccountant(tableEntity);
            if (workflowMetadata == null)
            {
                return new EmptyResult();
            }
            return new SystemTextJsonResult(workflowMetadata);
        }

        private static Accountant ToAccountant(TableEntity tableEntity)
        {
            if (tableEntity == null)
            {
                return null;
			}
			var serializerOptions = new JsonSerializerOptions().ConfigureAdaDtoInheritance();
			var tasks = tableEntity.GetString("Tasks");
			return new Accountant
			{
				Name = tableEntity.GetString("Name"),
				NearMainnet = tableEntity.GetString("NearMainnet"),
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
	                : JsonSerializer.Deserialize<List<AccountingTaskBase>>(tasks, serializerOptions)!
            };
        }
    }
}

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
using Toolblox.Cryptography;
using Toolblox.Model;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Amqp.Framing;
using System.IO;
using Azure.Storage.Blobs;
using System.Reflection.Metadata;
using System.Collections;
using Azure.Storage.Blobs.Models;

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
            var workflows = pages.SelectMany(x => x.Values).Select(TableEntityExtensions.ToAccountant).ToList();

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
				{ "AddressBookUrl", accountant.AddressBookUrl },
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
				{ "Contract", accountant.Contract },
				{ "Workflow", accountant.Workflow },
				{ "Logo", accountant.Logo },
				{ "ProcessFee", accountant.ProcessFee },
				{ "Tasks", JsonSerializer.Serialize(accountant.Tasks, serializerOptions) },
				{ "AddressBookAccessRights", JsonSerializer.Serialize(accountant.AddressBookAccessRights, serializerOptions) },
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

        [FunctionName("GeneratePublicKey")]
        public static async Task<IActionResult> GeneratePublicKey(
	        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Accountant/GeneratePublicKey")] HttpRequestMessage req,
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
			var newKeyPair = new Mnemonic().GetKeyPair(id);
			var client = new SecretClient(vaultUri: new Uri("https://adaaddressbookkeys.vault.azure.net/"), credential: new DefaultAzureCredential());
			await client.SetSecretAsync(new KeyVaultSecret($"ada{id}".Replace("-", ""), newKeyPair.Serialize()));
			return new OkObjectResult(newKeyPair.PublicKey);
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
            var workflowMetadata = tableEntity.ToAccountant();
            if (workflowMetadata == null)
            {
                return new EmptyResult();
            }
            return new SystemTextJsonResult(workflowMetadata);
		}

        [FunctionName("UploadLogo")]
        public static async Task<IActionResult> UploadLogo(
	        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "UploadLogo")] HttpRequestMessage req,
	        [Table("Accountants")] TableClient todoTable,
			ILogger log)
        {
	        log.LogInformation("C# HTTP trigger function processed a request.");
	        var query = req.RequestUri.ParseQueryString();
			var id = query.Get("id");
			var userId = await Security.GetUser(req);
			var tableEntity = await todoTable.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{userId.Sanitize()}' and RowKey eq '{id.Sanitize()}'").FirstOrDefaultAsync();
			if (tableEntity == null)
			{
				return new NotFoundObjectResult(null);
			}
			var blobConnection = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

	        var content = await req.Content.ReadAsMultipartAsync();

	        if (content.Contents.Count != 1 || content.Contents[0].Headers.ContentDisposition == null)
	        {
		        throw new ArgumentException("Content headers");
	        }
	        var fileName = content.Contents[0].Headers.ContentDisposition.FileName;
			var blobClient = new BlobContainerClient(blobConnection, "ada-logos");
			await blobClient.CreateIfNotExistsAsync(PublicAccessType.Blob);
			var logoFileName = $"{id.Replace("-", "")}{Guid.NewGuid().ToString().Substring(0, 8)}{fileName}";
			var blob = blobClient.GetBlobClient(logoFileName);
			await blob.UploadAsync(await content.Contents[0].ReadAsStreamAsync(), overwrite: true);
			return new JsonResult(new { cid = blob.Uri.AbsoluteUri });
        }
	}
}

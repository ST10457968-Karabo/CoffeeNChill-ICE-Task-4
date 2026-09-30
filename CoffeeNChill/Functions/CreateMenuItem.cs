using Azure;
using Azure.Data.Tables;
using CoffeeNChill.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace CoffeeNChill.Functions;

public class CreateMenuItem
{
    private const string TableName = "MenuItems";

    private readonly ILogger<CreateMenuItem> _logger;
    private readonly TableServiceClient _tableServiceClient;

    public CreateMenuItem(ILogger<CreateMenuItem> logger, TableServiceClient tableServiceClient)
    {
        _logger = logger;
        _tableServiceClient = tableServiceClient;
    }

    [Function("CreateMenuItem")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "menu")] HttpRequestData req)
    {
        _logger.LogInformation("CreateMenuItem triggered.");

        CreateMenuItemRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<CreateMenuItemRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return await BuildErrorResponse(req, HttpStatusCode.BadRequest, "Request body is not valid JSON.");
        }

        if (body is null)
        {
            return await BuildErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required.");
        }

        var validationError = Validate(body);
        if (validationError is not null)
        {
            return await BuildErrorResponse(req, HttpStatusCode.BadRequest, validationError);
        }

        var tableClient = _tableServiceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();

        var entity = new MenuItem
        {
            PartitionKey = body.Category!.Trim(),
            RowKey = body.Stock!.Trim(),
            Name = body.Name!.Trim(),
            Description = body.Description?.Trim() ?? string.Empty,
            Price = body.Price,
            IsAvailable = body.IsAvailable
        };

        try
        {
            
            await tableClient.AddEntityAsync(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Conflict)
        {
            return await BuildErrorResponse(
                req,
                HttpStatusCode.Conflict,
                //Stock is the Stock keeping unit Delson, don't get confused. You can remove this after reading!!
                $"A menu item with Stock '{entity.RowKey}' already exists in category '{entity.PartitionKey}'.");
        }

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(MenuItemMapper.ToDto(entity));
        return response;
    }

    private static string? Validate(CreateMenuItemRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Category)) return "Category is required.";
        if (string.IsNullOrWhiteSpace(body.Stock)) return "Stock is required.";
        if (string.IsNullOrWhiteSpace(body.Name)) return "Name is required.";
        if (body.Price <= 0) return "Price must be greater than zero.";
        return null;
    }

    private static async Task<HttpResponseData> BuildErrorResponse(
        HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}
// Custom code for the SupplyFlow BrasilAPI CNPJ connector.
// Runs inside the Power Platform connector runtime (.NET Standard, 2 min timeout, no external packages).
//  - Request: strips the CNPJ mask so makers can pass the value exactly as stored/typed.
//  - Response: maps BrasilAPI's snake_case Portuguese payload to a stable, documented English schema
//    (flows keep working if the upstream API adds/renames fields) and adds the derived "isActive" flag.
public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        if (this.Context.OperationId != "GetCompanyByCnpj")
        {
            return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        }

        var uri = this.Context.Request.RequestUri;
        var segments = uri.AbsolutePath.Split('/');
        var raw = Uri.UnescapeDataString(segments[segments.Length - 1]);
        var cnpj = new string(raw.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        if (cnpj.Length != 14)
        {
            return Error(HttpStatusCode.BadRequest, $"CNPJ deve ter 14 caracteres (recebido: '{raw}').");
        }

        segments[segments.Length - 1] = cnpj;
        this.Context.Request.RequestUri = new UriBuilder(uri) { Path = string.Join("/", segments) }.Uri;

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var source = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        var status = (string)source["descricao_situacao_cadastral"] ?? string.Empty;

        var result = new JObject
        {
            ["cnpj"] = cnpj,
            ["legalName"] = source["razao_social"],
            ["tradeName"] = source["nome_fantasia"],
            ["registrationStatus"] = status,
            ["isActive"] = string.Equals(status, "ATIVA", StringComparison.OrdinalIgnoreCase),
            ["registrationStatusDate"] = source["data_situacao_cadastral"],
            ["mainActivityCode"] = source["cnae_fiscal"]?.ToString(),
            ["mainActivity"] = source["cnae_fiscal_descricao"],
            ["companySize"] = source["porte"],
            ["street"] = Join(source["descricao_tipo_de_logradouro"], source["logradouro"]),
            ["number"] = source["numero"],
            ["complement"] = source["complemento"],
            ["district"] = source["bairro"],
            ["city"] = source["municipio"],
            ["state"] = source["uf"],
            ["postalCode"] = source["cep"]?.ToString(),
            ["phone"] = source["ddd_telefone_1"],
        };

        response.Content = CreateJsonContent(result.ToString());
        return response;
    }

    private static string Join(JToken first, JToken second)
    {
        return string.Join(" ", new[] { (string)first, (string)second }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string message)
    {
        return new HttpResponseMessage(status)
        {
            Content = CreateJsonContent(new JObject { ["message"] = message }.ToString()),
        };
    }
}

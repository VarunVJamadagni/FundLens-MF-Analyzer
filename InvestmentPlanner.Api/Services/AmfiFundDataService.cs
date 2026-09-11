using System.Globalization;
using System.Text.Json;
using InvestmentPlanner.Api.Models;

namespace InvestmentPlanner.Api.Services;

public class AmfiFundDataService
{
    private readonly string _navFilePath;
    private readonly string _jsonFilePath;

    public AmfiFundDataService()
    {
        var projectRoot = Directory.GetParent(AppContext.BaseDirectory)!
            .Parent!
            .Parent!
            .Parent!
            .Parent!
            .FullName;

        _navFilePath = Path.Combine(projectRoot, "Data", "NAVAll.txt");
        _jsonFilePath = Path.Combine(projectRoot, "Data", "funds.json");
    }

    public async Task GenerateFundsJsonAsync()
    {
        if (!File.Exists(_navFilePath))
        {
            throw new FileNotFoundException(
                $"NAVAll.txt was not found at: {_navFilePath}");
        }

        var lines = await File.ReadAllLinesAsync(_navFilePath);

        var funds = new List<AmfiFund>();

        string currentFundHouse = string.Empty;
        string currentCategory = string.Empty;
        string currentSubCategory = string.Empty;

        DateTime latestNavDate = DateTime.MinValue;

        // ---------------------------------------------------------
        // FIND LATEST NAV DATE
        // ---------------------------------------------------------

        foreach (var line in lines)
        {
            var parts = line.Split(';');

            if (parts.Length < 8)
                continue;

            if (DateTime.TryParseExact(
                    parts[7].Trim(),
                    "dd-MMM-yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var navDate))
            {
                if (navDate > latestNavDate)
                    latestNavDate = navDate;
            }
        }

        var activeDateCutoff = latestNavDate.AddDays(-7);

        // ---------------------------------------------------------
        // PARSE NAVAll.txt
        // ---------------------------------------------------------

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            // Skip column header.
            if (trimmedLine.StartsWith(
                    "Scheme Code;",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = trimmedLine.Split(';');

            // -----------------------------------------------------
            // FUND HOUSE
            // -----------------------------------------------------

            if (parts.Length == 1 &&
                trimmedLine.EndsWith(
                    "Mutual Fund",
                    StringComparison.OrdinalIgnoreCase))
            {
                currentFundHouse = trimmedLine;

                // Category belongs to the current AMC section.
                // Reset it when moving to another fund house.
                currentCategory = string.Empty;
                currentSubCategory = string.Empty;

                continue;
            }

            // -----------------------------------------------------
            // CATEGORY / SUB-CATEGORY HEADER
            // -----------------------------------------------------

            if (parts.Length == 1 &&
                IsCategoryHeader(trimmedLine))
            {
                ParseCategoryHeader(
                    trimmedLine,
                    out currentCategory,
                    out currentSubCategory);

                continue;
            }

            // -----------------------------------------------------
            // SCHEME
            // -----------------------------------------------------

            if (parts.Length < 8)
                continue;

            if (!int.TryParse(parts[0].Trim(), out _))
                continue;

            if (!DateTime.TryParseExact(
                    parts[7].Trim(),
                    "dd-MMM-yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var navDate))
            {
                continue;
            }

            // Ignore old/inactive schemes.
            if (navDate < activeDateCutoff)
                continue;

            var plan = NormalizePlan(parts[4].Trim());
            var option = NormalizeOption(parts[5].Trim());

            // Ignore explicitly defunct schemes.
            if (plan.Equals(
                    "Defunct",
                    StringComparison.OrdinalIgnoreCase) ||
                option.Equals(
                    "Defunct",
                    StringComparison.OrdinalIgnoreCase) ||
                parts[3].Contains(
                    "Defunct",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentFundHouse))
                continue;

            var fund = new AmfiFund
            {
                SchemeCode = parts[0].Trim(),
                FundHouse = currentFundHouse,
                SchemeName = parts[3].Trim(),

                Plan = plan,
                Option = option,

                Category = currentCategory,
                SubCategory = currentSubCategory,

                LastNavDate = navDate.ToString("dd-MMM-yyyy")
            };

            funds.Add(fund);
        }

        // ---------------------------------------------------------
        // REMOVE DUPLICATES
        // ---------------------------------------------------------

        funds = funds
            .GroupBy(x => x.SchemeCode)
            .Select(x => x.First())
            .OrderBy(x => x.FundHouse)
            .ThenBy(x => x.SchemeName)
            .ThenBy(x => x.Plan)
            .ThenBy(x => x.Option)
            .ToList();

        // ---------------------------------------------------------
        // WRITE funds.json
        // ---------------------------------------------------------

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(funds, jsonOptions);

        await File.WriteAllTextAsync(_jsonFilePath, json);

        Console.WriteLine(
            $"Generated {funds.Count} active schemes.");

        Console.WriteLine(
            $"Latest NAV date: {latestNavDate:dd-MMM-yyyy}");

        Console.WriteLine(
            $"Active cutoff: {activeDateCutoff:dd-MMM-yyyy}");

        Console.WriteLine(
            $"Output: {_jsonFilePath}");
    }

    // -------------------------------------------------------------
    // CATEGORY HEADER DETECTION
    // -------------------------------------------------------------

    private static bool IsCategoryHeader(string line)
    {
        var openParen = line.IndexOf('(');
        var closeParen = line.LastIndexOf(')');

        // A category header must contain a section name followed
        // by information inside parentheses.
        //
        // Examples:
        //
        // Open Ended Schemes(Equity Scheme - Large Cap Fund)
        // Exchange Traded Funds(Other Scheme - Other)
        //
        // This is deliberately broader than checking only for
        // "Schemes(" because AMFI has multiple scheme section types.

        return openParen > 0 &&
               closeParen > openParen;
    }

    // -------------------------------------------------------------
    // CATEGORY / SUB-CATEGORY PARSING
    // -------------------------------------------------------------

    private static void ParseCategoryHeader(
        string header,
        out string category,
        out string subCategory)
    {
        category = string.Empty;
        subCategory = string.Empty;

        var openParen = header.IndexOf('(');
        var closeParen = header.LastIndexOf(')');

        if (openParen < 0 || closeParen <= openParen)
            return;

        var categoryText = header[
            (openParen + 1)..closeParen
        ].Trim();

        if (string.IsNullOrWhiteSpace(categoryText))
            return;

        // Example:
        //
        // Open Ended Schemes(Equity Scheme - Large Cap Fund)
        //
        // Category    = Equity Scheme
        // SubCategory = Large Cap Fund

        var separatorIndex = categoryText.IndexOf(" - ");

        if (separatorIndex >= 0)
        {
            category = categoryText[..separatorIndex].Trim();
            subCategory = categoryText[(separatorIndex + 3)..].Trim();
        }
        else
        {
            // Example:
            //
            // Exchange Traded Funds(Other Scheme)
            //
            // Category    = Other Scheme
            // SubCategory = ""
            category = categoryText;
        }
    }

    // -------------------------------------------------------------
    // PLAN NORMALIZATION
    // -------------------------------------------------------------

    private static string NormalizePlan(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return "Other";

        if (plan.Contains(
                "Direct",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Direct";
        }

        if (plan.Contains(
                "Regular",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Regular";
        }

        if (plan.Contains(
                "Institutional",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Institutional";
        }

        if (plan.Contains(
                "Retail",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Retail";
        }

        return plan;
    }

    // -------------------------------------------------------------
    // OPTION NORMALIZATION
    // -------------------------------------------------------------

    private static string NormalizeOption(string option)
    {
        if (string.IsNullOrWhiteSpace(option))
            return "Other";

        if (option.Contains(
                "Growth",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Growth";
        }

        if (option.Contains(
                "IDCW",
                StringComparison.OrdinalIgnoreCase) ||
            option.Contains(
                "Dividend",
                StringComparison.OrdinalIgnoreCase))
        {
            return "IDCW";
        }

        return option;
    }

    // -------------------------------------------------------------
    // FUND HOUSE SEARCH
    // -------------------------------------------------------------

    public async Task<List<AmfiFund>> GetFundsByFundHouseAsync(
        string fundHouse)
    {
        if (!File.Exists(_jsonFilePath))
        {
            throw new FileNotFoundException(
                $"funds.json was not found at: {_jsonFilePath}");
        }

        var json = await File.ReadAllTextAsync(_jsonFilePath);

        var funds = JsonSerializer.Deserialize<List<AmfiFund>>(json)
                    ?? new List<AmfiFund>();

        var search = fundHouse.Trim();

        if (string.IsNullOrWhiteSpace(search))
            return new List<AmfiFund>();

        // First try an exact fund-house match.
        var exactMatches = funds
            .Where(f => f.FundHouse.Equals(
                search,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactMatches.Count > 0)
            return exactMatches;

        // If there is no exact match, allow a partial fund-house search.
        //
        // Example:
        // "ICICI Prudential" -> "ICICI Prudential Mutual Fund"
        // "HDFC"             -> "HDFC Mutual Fund"

        return funds
            .Where(f => f.FundHouse.Contains(
                search,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
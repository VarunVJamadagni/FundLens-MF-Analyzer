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

        if (latestNavDate == DateTime.MinValue)
        {
            throw new InvalidOperationException(
                "Could not determine the latest NAV date from NAVAll.txt.");
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
            // CATEGORY / SUB-CATEGORY HEADER
            // -----------------------------------------------------

            // IMPORTANT:
            // AMFI structure is:
            //
            // Category Header
            //      ↓
            // Fund House
            //      ↓
            // Schemes
            //
            // Therefore category must be set BEFORE the fund house
            // and must NOT be cleared when the fund house changes.

            if (parts.Length == 1 &&
                TryParseCategoryHeader(
                    trimmedLine,
                    out var category,
                    out var subCategory))
            {
                currentCategory = category;
                currentSubCategory = subCategory;

                continue;
            }

            // -----------------------------------------------------
            // FUND HOUSE
            // -----------------------------------------------------

            if (parts.Length == 1 &&
                trimmedLine.EndsWith(
                    "Mutual Fund",
                    StringComparison.OrdinalIgnoreCase))
            {
                currentFundHouse = trimmedLine;

                // DO NOT reset currentCategory/currentSubCategory.
                //
                // Multiple fund houses belong to the same category
                // until the next category header appears.

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

        // ---------------------------------------------------------
        // GENERATION SUMMARY
        // ---------------------------------------------------------

        var categoryCount = funds
            .Count(x => !string.IsNullOrWhiteSpace(x.Category));

        var subCategoryCount = funds
            .Count(x => !string.IsNullOrWhiteSpace(x.SubCategory));

        var distinctCategories = funds
            .Where(x => !string.IsNullOrWhiteSpace(x.Category))
            .Select(x => x.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var distinctSubCategories = funds
            .Where(x => !string.IsNullOrWhiteSpace(x.SubCategory))
            .Select(x => x.SubCategory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Console.WriteLine(
            $"Generated {funds.Count} active schemes.");

        Console.WriteLine(
            $"Latest NAV date: {latestNavDate:dd-MMM-yyyy}");

        Console.WriteLine(
            $"Active cutoff: {activeDateCutoff:dd-MMM-yyyy}");

        Console.WriteLine(
            $"Funds with Category: {categoryCount}");

        Console.WriteLine(
            $"Funds with SubCategory: {subCategoryCount}");

        Console.WriteLine(
            $"Distinct Categories: {distinctCategories}");

        Console.WriteLine(
            $"Distinct SubCategories: {distinctSubCategories}");

        Console.WriteLine(
            $"Output: {_jsonFilePath}");
    }

    // -------------------------------------------------------------
    // CATEGORY / SUB-CATEGORY PARSING
    // -------------------------------------------------------------

    private static bool TryParseCategoryHeader(
        string header,
        out string category,
        out string subCategory)
    {
        category = string.Empty;
        subCategory = string.Empty;

        var openParen = header.IndexOf('(');
        var closeParen = header.LastIndexOf(')');

        if (openParen <= 0 ||
            closeParen <= openParen)
        {
            return false;
        }

        var categoryText = header[
            (openParen + 1)..closeParen
        ].Trim();

        if (string.IsNullOrWhiteSpace(categoryText))
            return false;

        var separatorIndex = categoryText.IndexOf(
            " - ",
            StringComparison.Ordinal);

        if (separatorIndex >= 0)
        {
            category = categoryText[..separatorIndex].Trim();

            subCategory = categoryText[
                (separatorIndex + 3)..
            ].Trim();

            return !string.IsNullOrWhiteSpace(category);
        }

        category = categoryText;

        return true;
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
        var funds = await LoadFundsAsync();

        var search = NormalizeSearchTerm(fundHouse);

        if (string.IsNullOrWhiteSpace(search))
            return new List<AmfiFund>();

        return funds
            .Where(f =>
                NormalizeSearchTerm(f.FundHouse)
                    .Contains(
                        search,
                        StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // -------------------------------------------------------------
    // GENERAL SEARCH
    // -------------------------------------------------------------

    public async Task<List<AmfiFund>> SearchFundsAsync(string query)
    {
        var funds = await LoadFundsAsync();

        var search = NormalizeSearchTerm(query);

        if (string.IsNullOrWhiteSpace(search))
            return new List<AmfiFund>();

        return funds
            .Where(f =>
                ContainsNormalized(f.FundHouse, search) ||
                ContainsNormalized(f.SchemeName, search) ||
                ContainsNormalized(f.Category, search) ||
                ContainsNormalized(f.SubCategory, search))
            .ToList();
    }

    // -------------------------------------------------------------
    // LOAD LOCAL FUND CATALOGUE
    // -------------------------------------------------------------

    public async Task<List<AmfiFund>> LoadFundsAsync()
    {
        if (!File.Exists(_jsonFilePath))
        {
            throw new FileNotFoundException(
                $"funds.json was not found at: {_jsonFilePath}");
        }

        var json = await File.ReadAllTextAsync(_jsonFilePath);

        return JsonSerializer.Deserialize<List<AmfiFund>>(json)
               ?? new List<AmfiFund>();
    }

    // -------------------------------------------------------------
    // SEARCH NORMALIZATION
    // -------------------------------------------------------------

    private static bool ContainsNormalized(
        string? value,
        string normalizedSearch)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return NormalizeSearchTerm(value)
            .Contains(
                normalizedSearch,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSearchTerm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(
            " ",
            value
                .Trim()
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries));
    }
}
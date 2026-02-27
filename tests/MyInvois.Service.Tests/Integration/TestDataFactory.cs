using MyInvois.Service.Models;

namespace MyInvois.Service.Tests.Integration;

/// <summary>
/// Factory for creating realistic test data across integration and E2E tests.
/// Uses skill: integration/myinvois-validator v1.0+ (valid data per LHDNM spec)
/// </summary>
public static class TestDataFactory
{
    /// <summary>
    /// Create a fully valid MovexInvoice (Sales type) that passes all validators
    /// </summary>
    public static MovexInvoice CreateValidSalesInvoice(string? invoiceNumber = null)
    {
        var invNum = invoiceNumber ?? $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6]}";

        return new MovexInvoice
        {
            InvoiceNumber = invNum,
            InvoiceDate = DateTime.UtcNow.ToString("yyyyMMdd"),
            InvoiceType = "Sales",
            CompanyCode = "100",
            CurrencyCode = "MYR",
            ExchangeRate = 1.0m,
            TotalExclTax = 1000.00m,
            TotalTax = 60.00m,
            TotalInclTax = 1060.00m,
            Supplier = new InvoiceParty
            {
                TIN = "C00000000000",
                Name = "SRX Engineering Sdn Bhd",
                BRN = "202301012345",
                IdScheme = "BRN",
                Address = "123 Jalan Utama, KL"
            },
            Buyer = new InvoiceParty
            {
                TIN = "C99999999999",
                Name = "Test Customer Sdn Bhd",
                BRN = "202301054321",
                IdScheme = "BRN",
                Address = "456 Jalan Besar, Penang"
            },
            Lines = new List<InvoiceLine>
            {
                new()
                {
                    LineNumber = 1,
                    ItemNumber = "SVC-001",
                    Description = "Engineering Consultancy Services",
                    ClassificationCode = "022",
                    Quantity = 10,
                    UnitOfMeasure = "EA",
                    UnitPrice = 100.00m,
                    LineTotal = 1000.00m,
                    TaxCode = "01",
                    TaxRate = 6.0m,
                    TaxAmount = 60.00m
                }
            }
        };
    }

    /// <summary>
    /// Create a valid Purchase invoice
    /// </summary>
    public static MovexInvoice CreateValidPurchaseInvoice(string? invoiceNumber = null)
    {
        var inv = CreateValidSalesInvoice(invoiceNumber);
        inv.InvoiceType = "Purchase";
        return inv;
    }

    /// <summary>
    /// Create a multi-line invoice
    /// </summary>
    public static MovexInvoice CreateMultiLineInvoice(int lineCount)
    {
        var inv = CreateValidSalesInvoice();

        inv.Lines = Enumerable.Range(1, lineCount).Select(i => new InvoiceLine
        {
            LineNumber = i,
            ItemNumber = $"ITEM-{i:D3}",
            Description = $"Line item {i}",
            ClassificationCode = "022",
            Quantity = i,
            UnitOfMeasure = "EA",
            UnitPrice = 100.00m,
            LineTotal = i * 100.00m,
            TaxCode = "01",
            TaxRate = 6.0m,
            TaxAmount = i * 6.00m
        }).ToList();

        inv.TotalExclTax = inv.Lines.Sum(l => l.LineTotal);
        inv.TotalTax = inv.Lines.Sum(l => l.TaxAmount);
        inv.TotalInclTax = inv.TotalExclTax + inv.TotalTax;

        return inv;
    }

    /// <summary>
    /// Create an invoice with validation errors (missing mandatory fields)
    /// </summary>
    public static MovexInvoice CreateInvalidInvoice_MissingFields()
    {
        return new MovexInvoice
        {
            InvoiceNumber = "",
            InvoiceDate = "",
            InvoiceType = "Sales",
            CompanyCode = "100",
            CurrencyCode = "MYR",
            ExchangeRate = 1.0m,
            TotalExclTax = 0m,
            TotalTax = 0m,
            TotalInclTax = 0m,
            Supplier = null,
            Buyer = null,
            Lines = new List<InvoiceLine>()
        };
    }

    /// <summary>
    /// Create an invoice with mismatched totals
    /// </summary>
    public static MovexInvoice CreateInvalidInvoice_MismatchedTotals()
    {
        var inv = CreateValidSalesInvoice();
        inv.TotalExclTax = 999.99m; // Wrong - doesn't match line totals
        return inv;
    }

    /// <summary>
    /// Create a batch of invoices with mix of valid and invalid
    /// </summary>
    public static List<MovexInvoice> CreateMixedBatch(int validCount, int invalidCount)
    {
        var batch = new List<MovexInvoice>();

        for (int i = 0; i < validCount; i++)
        {
            batch.Add(CreateValidSalesInvoice($"VALID-{i + 1:D3}"));
        }

        for (int i = 0; i < invalidCount; i++)
        {
            var invalid = CreateInvalidInvoice_MissingFields();
            invalid.InvoiceNumber = $"INVALID-{i + 1:D3}";
            batch.Add(invalid);
        }

        return batch;
    }

    /// <summary>
    /// Create a valid MyInvoiceDocument (already transformed)
    /// </summary>
    public static MyInvoiceDocument CreateValidDocument(string? invoiceNumber = null)
    {
        var invNum = invoiceNumber ?? "INV-TEST-001";

        return new MyInvoiceDocument
        {
            InvoiceNumber = invNum,
            SourceInvoiceNumber = invNum,
            IssueDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            IssueTime = DateTime.UtcNow.ToString("HH:mm:ss"),
            CurrencyCode = "MYR",
            ExchangeRate = 1.0m,
            SupplierTIN = "C00000000000",
            SupplierName = "SRX Engineering Sdn Bhd",
            SupplierBRN = "202301012345",
            BuyerTIN = "C99999999999",
            BuyerName = "Test Customer Sdn Bhd",
            TotalExclTax = 1000.00m,
            TotalTax = 60.00m,
            TotalInclTax = 1060.00m,
            PayableAmount = 1060.00m,
            Lines = new List<MyInvoiceLine>
            {
                new()
                {
                    LineNumber = 1,
                    ItemNumber = "SVC-001",
                    Description = "Engineering Consultancy Services",
                    ClassificationCode = "022",
                    Quantity = 10,
                    UnitOfMeasure = "EA",
                    UnitPrice = 100.00m,
                    LineTotalExclTax = 1000.00m,
                    TaxCode = "01",
                    TaxRate = 6.0m,
                    TaxAmount = 60.00m,
                    LineTotalInclTax = 1060.00m
                }
            }
        };
    }

    /// <summary>
    /// Create a successful SubmissionResult
    /// </summary>
    public static SubmissionResult CreateSuccessResult(string invoiceNumber)
    {
        return new SubmissionResult
        {
            InvoiceNumber = invoiceNumber,
            Status = "Success",
            MyInvoisUUID = Guid.NewGuid().ToString(),
            MyInvoisStatus = "Valid",
            SubmittedAt = DateTime.UtcNow,
            DurationMs = 250
        };
    }

    /// <summary>
    /// Create a failed SubmissionResult
    /// </summary>
    public static SubmissionResult CreateFailedResult(string invoiceNumber, string errorCode, string errorMessage)
    {
        return new SubmissionResult
        {
            InvoiceNumber = invoiceNumber,
            Status = "Failed",
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            SubmittedAt = DateTime.UtcNow,
            DurationMs = 100
        };
    }
}

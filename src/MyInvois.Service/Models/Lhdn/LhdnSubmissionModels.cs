namespace MyInvois.Service.Models.Lhdn;

using System.Text.Json.Serialization;

/// <summary>
/// LHDN /api/v1.0/documentsubmissions response envelope.
/// HTTP 200 is returned even when documents are rejected (Step 07 sync only).
/// Always check RejectedDocuments — empty list + HTTP 200 = Step 07 accepted.
/// Field-level errors appear via Step 08 async poll (2–5 min later).
/// </summary>
internal sealed class SubmissionResponse
{
    [JsonPropertyName("submissionUid")]
    public string? SubmissionUid { get; set; }

    [JsonPropertyName("acceptedDocuments")]
    public List<AcceptedDocument>? AcceptedDocuments { get; set; }

    [JsonPropertyName("rejectedDocuments")]
    public List<RejectedDocument>? RejectedDocuments { get; set; }
}

internal sealed class AcceptedDocument
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("invoiceCodeNumber")]
    public string? InvoiceCodeNumber { get; set; }
}

internal sealed class RejectedDocument
{
    [JsonPropertyName("invoiceCodeNumber")]
    public string? InvoiceCodeNumber { get; set; }

    [JsonPropertyName("error")]
    public LhdnError? Error { get; set; }
}

internal sealed class LhdnError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("details")]
    public List<LhdnErrorDetail>? Details { get; set; }
}

internal sealed class LhdnErrorDetail
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }
}

/// <summary>
/// LHDN GET /api/v1.0/documents/{uuid}/details response.
/// Step 08 async validator writes the final status here 2–5 minutes after submission.
/// Status values: "Valid", "Invalid", "Cancelled", "Submitted" (still processing).
/// </summary>
internal sealed class DocumentDetailsResponse
{
    [JsonPropertyName("uuid")]              public string?  Uuid              { get; set; }
    [JsonPropertyName("submissionUid")]     public string?  SubmissionUid     { get; set; }
    [JsonPropertyName("longId")]            public string?  LongId            { get; set; }
    [JsonPropertyName("internalId")]        public string?  InternalId        { get; set; }
    [JsonPropertyName("typeName")]          public string?  TypeName          { get; set; }
    [JsonPropertyName("typeVersionName")]   public string?  TypeVersionName   { get; set; }
    [JsonPropertyName("issuerTin")]         public string?  IssuerTin         { get; set; }
    [JsonPropertyName("issuerName")]        public string?  IssuerName        { get; set; }
    [JsonPropertyName("receiverId")]        public string?  ReceiverId        { get; set; }
    [JsonPropertyName("receiverName")]      public string?  ReceiverName      { get; set; }
    [JsonPropertyName("dateTimeIssued")]    public string?  DateTimeIssued    { get; set; }
    [JsonPropertyName("dateTimeReceived")]  public string?  DateTimeReceived  { get; set; }
    [JsonPropertyName("dateTimeValidated")] public string?  DateTimeValidated { get; set; }
    [JsonPropertyName("totalSales")]        public decimal? TotalSales        { get; set; }
    [JsonPropertyName("totalDiscount")]     public decimal? TotalDiscount     { get; set; }
    [JsonPropertyName("netAmount")]         public decimal? NetAmount         { get; set; }
    [JsonPropertyName("total")]             public decimal? Total             { get; set; }
    [JsonPropertyName("status")]            public string?  Status            { get; set; }
    [JsonPropertyName("createdByUserId")]   public string?  CreatedByUserId   { get; set; }
}

internal sealed class ErrorResponse
{
    [JsonPropertyName("error")]
    public ErrorDetail? Error { get; set; }

    internal sealed class ErrorDetail
    {
        [JsonPropertyName("code")]    public string Code    { get; set; } = string.Empty;
        [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    }
}

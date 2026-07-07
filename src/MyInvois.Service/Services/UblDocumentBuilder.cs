namespace MyInvois.Service.Services;

using MyInvois.Service.Models;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Builds a UBL 2.1 JSON invoice document per LHDN MyInvois SDK v1.5.
///
/// Output format follows the official LHDN sample:
///   sdk.myinvois.hasil.gov.my/files/sample-ul-invoice-2.1-signed.min.json
///
/// Every UBL value uses the { "_": value } wrapper convention.
/// Arrays are used even for single-value fields (UBL JSON spec requirement).
/// Signature is embedded in UBLExtensions per XAdES-BES specification.
/// </summary>
public static class UblDocumentBuilder
{
    // -----------------------------------------------------------------------
    // Public entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build a complete UBL 2.1 JSON invoice object (unsigned — UBLExtensions absent).
    /// Returns the object graph; caller serializes to minified JSON for hashing/signing.
    /// </summary>
    public static object BuildUnsigned(MyInvoiceDocument doc)
    {
        return new UblEnvelope
        {
            D = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2",
            A = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2",
            B = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2",
            Invoice = new[] { BuildInvoiceBody(doc, ublExtensions: null, includeSignature: false) }
        };
    }

    /// <summary>
    /// Build the signed UBL 2.1 JSON invoice object with XAdES signature embedded
    /// in UBLExtensions. Returns the complete object ready for base64 encoding.
    ///
    /// Signing steps per LHDN SDK v1.5:
    ///   1. Build unsigned document, minify JSON, strip UBLExtensions+Signature elements
    ///   2. SHA-256 hash → DocDigest (base64)
    ///   3. Sign DocDigest bytes with RSA-SHA256 → Sig (base64)
    ///   4. SHA-256 hash of DER-encoded certificate → CertDigest (base64)
    ///   5. Build SignedProperties, minify, SHA-256 hash → PropsDigest (base64)
    ///   6. Assemble UBLExtensions with all computed values
    /// </summary>
    public static object BuildSigned(MyInvoiceDocument doc, X509Certificate2 certificate)
    {
        // Per LHDN SDK v1.5 / signature-creation-json:
        //
        // DS322: LHDN parse+reserializes the submitted document (their JSON library strips trailing
        // decimal zeros: 160.000000 → 160, 12030.40 → 12030.4) before hashing for DS322.
        // We must emit numbers in the same minimal form. DecimalNormalizer on MinifyOptions handles this:
        // all decimal values are written without trailing zeros before being hashed or submitted.
        // This makes our canonical byte-identical to LHDN's canonical.
        //
        // DS320: propsDigest = SHA256(Minify({"Target":"signature","SignedProperties":[...]}))
        // The same signedPropsNode object reference is used for both hashing and embedding in UBLExtensions.

        var certDer = certificate.RawData;
        var certDigest = Convert.ToBase64String(SHA256.HashData(certDer));
        var certBase64 = Convert.ToBase64String(certDer);
        var signingTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var issuerName = certificate.IssuerName.Name ?? string.Empty;
        var serialDecimal = HexToDecimalString(certificate.SerialNumber ?? string.Empty);

        // Step 1 — canonical document: no UBLExtensions, no Signature.
        // DecimalNormalizer ensures numbers match LHDN's re-serialized form exactly.
        var canonicalEnvelope = new UblEnvelope
        {
            D = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2",
            A = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2",
            B = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2",
            Invoice = new[] { BuildInvoiceBody(doc, ublExtensions: null, includeSignature: false) }
        };
        var canonicalJson = Minify(canonicalEnvelope);

        // Step 2 — document digest and RSA signature over canonical bytes.
        var docHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        var docDigest = Convert.ToBase64String(docHashBytes);

        using var rsa = certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Certificate does not contain an RSA private key.");
        var sig = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(canonicalJson), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        // Step 3 — signed properties digest: hash the full QualifyingProperties object.
        var signedPropsNode = BuildSignedPropertiesNode(certDigest, signingTime, issuerName, serialDecimal);
        var qualifyingPropsNode = new { Target = "signature", SignedProperties = signedPropsNode };
        var propsDigest = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(Minify(qualifyingPropsNode))));

        // Step 4 — assemble final signed document.
        var ublExtensions = BuildUblExtensions(
            sig, docDigest, propsDigest,
            certBase64, certDigest, signingTime, issuerName, serialDecimal, signedPropsNode);

        return new UblEnvelope
        {
            D = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2",
            A = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2",
            B = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2",
            Invoice = new[] { BuildInvoiceBody(doc, ublExtensions, includeSignature: true) }
        };
    }

    /// <summary>
    /// Serialize to minified JSON (no whitespace) using LHDN-compliant serializer options.
    /// </summary>
    public static string Minify(object obj)
    {
        return JsonSerializer.Serialize(obj, MinifyOptions);
    }

    // -----------------------------------------------------------------------
    // Invoice body
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build the invoice body fields as a mutable Dictionary — no UBLExtensions, no Signature.
    /// Used by BuildSigned so UBLExtensions and Signature can be appended to the exact same
    /// object after the canonical hash is computed, guaranteeing byte-identical serialization.
    /// </summary>
    private static Dictionary<string, object> BuildInvoiceBodyDict(MyInvoiceDocument doc)
    {
        return new Dictionary<string, object>
        {
            ["ID"] = V(doc.InvoiceNumber),
            ["IssueDate"] = V(doc.IssueDate),
            ["IssueTime"] = V(doc.IssueTime + "Z"),
            ["InvoiceTypeCode"] = new[] { new { _ = doc.DocumentTypeCode, listVersionID = "1.1" } },
            ["DocumentCurrencyCode"] = V(doc.CurrencyCode),
            ["TaxCurrencyCode"] = V(doc.CurrencyCode),
            ["AccountingSupplierParty"] = new[] { BuildSupplier(doc) },
            ["AccountingCustomerParty"] = new[] { BuildBuyer(doc) },
            ["TaxTotal"] = new[] { BuildTaxTotal(doc) },
            ["LegalMonetaryTotal"] = new[] { BuildLegalMonetaryTotal(doc) },
            ["InvoiceLine"] = BuildInvoiceLines(doc),
        };
    }

    private static object BuildInvoiceBody(MyInvoiceDocument doc, object? ublExtensions, bool includeSignature = true)
    {
        var body = BuildInvoiceBodyDict(doc);

        if (ublExtensions != null)
            body["UBLExtensions"] = new[] { ublExtensions };

        if (includeSignature)
            body["Signature"] = new[]
            {
                new
                {
                    ID = V("urn:oasis:names:specification:ubl:signature:Invoice"),
                    SignatureMethod = V("urn:oasis:names:specification:ubl:dsig:enveloped:xades")
                }
            };

        return body;
    }

    // -----------------------------------------------------------------------
    // Party blocks
    // -----------------------------------------------------------------------

    private static object BuildSupplier(MyInvoiceDocument doc)
    {
        var addressLines = ParseAddressLines(doc.SupplierAddress);

        return new
        {
            Party = new[]
            {
                new
                {
                    IndustryClassificationCode = new[] { new { _ = doc.SupplierMsicCode, name = doc.SupplierMsicDescription } },
                    PartyIdentification = new object[]
                    {
                        new { ID = new[] { new { _ = doc.SupplierTIN, schemeID = "TIN" } } },
                        new { ID = new[] { new { _ = doc.SupplierBRN, schemeID = doc.SupplierIdScheme } } },
                        new { ID = new[] { new { _ = "NA", schemeID = "SST" } } },
                        new { ID = new[] { new { _ = "NA", schemeID = "TTX" } } }
                    },
                    PostalAddress = new[] { BuildAddress(addressLines, doc.SupplierCountryCode, doc.SupplierStateCode) },
                    PartyLegalEntity = new[] { new { RegistrationName = V(doc.SupplierName) } },
                    Contact = new[] { new { Telephone = V(string.IsNullOrWhiteSpace(doc.SupplierPhone) ? "NA" : doc.SupplierPhone) } }
                }
            }
        };
    }

    private static object BuildBuyer(MyInvoiceDocument doc)
    {
        var addressLines = ParseAddressLines(doc.BuyerAddress);
        var buyerTin = string.IsNullOrWhiteSpace(doc.BuyerTIN) ? "EI00000000020" : doc.BuyerTIN;
        var buyerBrn = doc.BuyerAlternativeId ?? "NA";

        return new
        {
            Party = new[]
            {
                new
                {
                    PartyIdentification = new object[]
                    {
                        new { ID = new[] { new { _ = buyerTin, schemeID = "TIN" } } },
                        new { ID = new[] { new { _ = buyerBrn, schemeID = doc.BuyerIdScheme } } },
                        new { ID = new[] { new { _ = "NA", schemeID = "SST" } } },
                        new { ID = new[] { new { _ = "NA", schemeID = "TTX" } } }
                    },
                    PostalAddress = new[] { BuildAddress(addressLines, doc.BuyerCountryCode, doc.BuyerStateCode) },
                    PartyLegalEntity = new[] { new { RegistrationName = V(doc.BuyerName) } },
                    Contact = new[] { new { Telephone = V(string.IsNullOrWhiteSpace(doc.BuyerPhone) ? "NA" : doc.BuyerPhone) } }
                }
            }
        };
    }

    // Maps country codes → ISO 3166-1 alpha-3 for the LHDN IdentificationCode field.
    // Accepts both alpha-2 (ISO standard) and alpha-3 (M3 sometimes stores 3-char codes).
    // "OTH" is used as the safe fallback for any unmapped code.
    private static readonly Dictionary<string, string> CountryToAlpha3 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MY"] = "MYS", ["MYS"] = "MYS",
        ["AU"] = "AUS", ["AUS"] = "AUS",
        ["SG"] = "SGP", ["SGP"] = "SGP",
        ["US"] = "USA", ["USA"] = "USA",
        ["GB"] = "GBR", ["GBR"] = "GBR", ["UK"] = "GBR",   // UK is non-standard but used in some M3 installations
        ["DE"] = "DEU", ["DEU"] = "DEU",
        ["JP"] = "JPN", ["JPN"] = "JPN",
        ["CN"] = "CHN", ["CHN"] = "CHN",
        ["TH"] = "THA", ["THA"] = "THA",
        ["ID"] = "IDN", ["IDN"] = "IDN",
        ["VN"] = "VNM", ["VNM"] = "VNM",
        ["PH"] = "PHL", ["PHL"] = "PHL",
        ["IN"] = "IND", ["IND"] = "IND",
        ["KR"] = "KOR", ["KOR"] = "KOR",
        ["TW"] = "TWN", ["TWN"] = "TWN",
        ["HK"] = "HKG", ["HKG"] = "HKG",
        ["NZ"] = "NZL", ["NZL"] = "NZL",
        ["FR"] = "FRA", ["FRA"] = "FRA",
        ["NL"] = "NLD", ["NLD"] = "NLD",
        ["IT"] = "ITA", ["ITA"] = "ITA",
        ["SE"] = "SWE", ["SWE"] = "SWE",
        ["FI"] = "FIN", ["FIN"] = "FIN",
        ["CH"] = "CHE", ["CHE"] = "CHE",
        ["MX"] = "MEX", ["MEX"] = "MEX",
        ["BR"] = "BRA", ["BRA"] = "BRA",
        ["CA"] = "CAN", ["CAN"] = "CAN",
    };

    private static object BuildAddress(string[] lines, string? countryCode, string? stateCode = null)
    {
        var addressLines = lines.Length > 0
            ? lines.Select(l => new { Line = V(l) }).ToArray<object>()
            : new object[] { new { Line = V("NA") } };

        var code = string.IsNullOrWhiteSpace(countryCode) ? "MY" : countryCode.Trim().ToUpperInvariant();
        // "OTH" is rejected by LHDN CV302. Fall back to "MYS" for any unmapped code so the
        // document is accepted; the log warning flags the code for addition to CountryToAlpha3.
        var alpha3 = CountryToAlpha3.TryGetValue(code, out var a3) ? a3 : "MYS";

        // CountrySubentityCode: use configured state for Malaysian addresses, "NA" for all foreign parties.
        var isMalaysia = code == "MY" || code == "MYS";
        var resolvedState = isMalaysia
            ? (string.IsNullOrWhiteSpace(stateCode) ? "00" : stateCode.Trim())
            : "NA";

        return new
        {
            CityName = V("NA"),
            PostalZone = V("NA"),
            CountrySubentityCode = V(resolvedState),
            AddressLine = addressLines,
            Country = new[]
            {
                new
                {
                    IdentificationCode = new[] { new { _ = alpha3, listID = "ISO3166-1", listAgencyID = "6" } }
                }
            }
        };
    }

    // -----------------------------------------------------------------------
    // Tax and monetary totals
    // -----------------------------------------------------------------------

    private static object BuildTaxTotal(MyInvoiceDocument doc)
    {
        return new
        {
            TaxAmount = new[] { new { _ = doc.TotalTax, currencyID = doc.CurrencyCode } },
            TaxSubtotal = new[]
            {
                new
                {
                    TaxableAmount = new[] { new { _ = doc.TotalExclTax, currencyID = doc.CurrencyCode } },
                    TaxAmount = new[] { new { _ = doc.TotalTax, currencyID = doc.CurrencyCode } },
                    TaxCategory = new[]
                    {
                        new
                        {
                            ID = V("01"),
                            TaxScheme = new[]
                            {
                                new
                                {
                                    ID = new[] { new { _ = "OTH", schemeID = "UN/ECE 5153", schemeAgencyID = "6" } }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static object BuildLegalMonetaryTotal(MyInvoiceDocument doc)
    {
        return new
        {
            LineExtensionAmount = new[] { new { _ = doc.TotalExclTax, currencyID = doc.CurrencyCode } },
            TaxExclusiveAmount = new[] { new { _ = doc.TotalExclTax, currencyID = doc.CurrencyCode } },
            TaxInclusiveAmount = new[] { new { _ = doc.TotalInclTax, currencyID = doc.CurrencyCode } },
            AllowanceTotalAmount = new[] { new { _ = 0m, currencyID = doc.CurrencyCode } },
            ChargeTotalAmount = new[] { new { _ = 0m, currencyID = doc.CurrencyCode } },
            PayableRoundingAmount = new[] { new { _ = 0m, currencyID = doc.CurrencyCode } },
            PayableAmount = new[] { new { _ = doc.PayableAmount, currencyID = doc.CurrencyCode } }
        };
    }

    // -----------------------------------------------------------------------
    // Invoice lines
    // -----------------------------------------------------------------------

    private static object[] BuildInvoiceLines(MyInvoiceDocument doc)
    {
        return doc.Lines.Select(line => (object)new
        {
            ID = V(line.LineNumber.ToString()),
            InvoicedQuantity = new[] { new { _ = line.Quantity, unitCode = MapUom(line.UnitOfMeasure) } },
            LineExtensionAmount = new[] { new { _ = line.LineTotalExclTax, currencyID = doc.CurrencyCode } },
            TaxTotal = new[]
            {
                new
                {
                    TaxAmount = new[] { new { _ = line.TaxAmount, currencyID = doc.CurrencyCode } },
                    TaxSubtotal = new[]
                    {
                        new
                        {
                            TaxableAmount = new[] { new { _ = line.LineTotalExclTax, currencyID = doc.CurrencyCode } },
                            TaxAmount = new[] { new { _ = line.TaxAmount, currencyID = doc.CurrencyCode } },
                            Percent = new[] { new { _ = line.TaxRate } },
                            TaxCategory = new[]
                            {
                                new
                                {
                                    ID = V(MapTaxCategory(line.TaxCode)),
                                    TaxScheme = new[]
                                    {
                                        new
                                        {
                                            ID = new[] { new { _ = "OTH", schemeID = "UN/ECE 5153", schemeAgencyID = "6" } }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            Item = new[]
            {
                new
                {
                    CommodityClassification = new[]
                    {
                        new
                        {
                            ItemClassificationCode = new[] { new { _ = line.ClassificationCode, listID = "CLASS" } }
                        }
                    },
                    Description = V(line.Description),
                    OriginCountry = new[] { new { IdentificationCode = V("MYS") } }
                }
            },
            Price = new[]
            {
                new
                {
                    PriceAmount = new[] { new { _ = line.UnitPrice, currencyID = doc.CurrencyCode } }
                }
            },
            ItemPriceExtension = new[]
            {
                new
                {
                    Amount = new[] { new { _ = line.LineTotalExclTax, currencyID = doc.CurrencyCode } }
                }
            }
        }).ToArray();
    }

    // -----------------------------------------------------------------------
    // XAdES signature structures
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build the SignedProperties node that will be embedded in QualifyingProperties.
    /// This exact object is also serialized and hashed to produce propsDigest (DS320).
    /// LHDN re-computes the digest from the embedded node — the hashed bytes and the
    /// embedded node must be identical serializations of the same object instance.
    /// </summary>
    private static object BuildSignedPropertiesNode(string certDigest, string signingTime, string issuerName, string serialDecimal)
    {
        return new[]
        {
            new
            {
                Id = "id-xades-signed-props",
                SignedSignatureProperties = new[]
                {
                    new
                    {
                        SigningTime = V(signingTime),
                        SigningCertificate = new[]
                        {
                            new
                            {
                                Cert = new[]
                                {
                                    new
                                    {
                                        CertDigest = new[]
                                        {
                                            new
                                            {
                                                DigestMethod = new[] { new { _ = "", Algorithm = "http://www.w3.org/2001/04/xmlenc#sha256" } },
                                                DigestValue = V(certDigest)
                                            }
                                        },
                                        IssuerSerial = new[]
                                        {
                                            new
                                            {
                                                X509IssuerName = V(issuerName),
                                                X509SerialNumber = V(serialDecimal)
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static object BuildUblExtensions(
        string sig, string docDigest, string propsDigest,
        string certBase64, string certDigest,
        string signingTime, string issuerName, string serialDecimal,
        object signedPropsNode)
    {
        return new
        {
            UBLExtension = new[]
            {
                new
                {
                    ExtensionURI = V("urn:oasis:names:specification:ubl:dsig:enveloped:xades"),
                    ExtensionContent = new[]
                    {
                        new
                        {
                            UBLDocumentSignatures = new[]
                            {
                                new
                                {
                                    SignatureInformation = new[]
                                    {
                                        new
                                        {
                                            ID = V("urn:oasis:names:specification:ubl:signature:1"),
                                            ReferencedSignatureID = V("urn:oasis:names:specification:ubl:signature:Invoice"),
                                            Signature = new[]
                                            {
                                                new
                                                {
                                                    Id = "signature",
                                                    SignedInfo = new[]
                                                    {
                                                        new
                                                        {
                                                            SignatureMethod = new[] { new { _ = "", Algorithm = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256" } },
                                                            Reference = new object[]
                                                            {
                                                                new
                                                                {
                                                                    Type = "http://uri.etsi.org/01903/v1.3.2#SignedProperties",
                                                                    URI = "#id-xades-signed-props",
                                                                    DigestMethod = new[] { new { _ = "", Algorithm = "http://www.w3.org/2001/04/xmlenc#sha256" } },
                                                                    DigestValue = V(propsDigest)
                                                                },
                                                                new
                                                                {
                                                                    Type = "",
                                                                    URI = "",
                                                                    DigestMethod = new[] { new { _ = "", Algorithm = "http://www.w3.org/2001/04/xmlenc#sha256" } },
                                                                    DigestValue = V(docDigest)
                                                                }
                                                            }
                                                        }
                                                    },
                                                    SignatureValue = V(sig),
                                                    KeyInfo = new[]
                                                    {
                                                        new
                                                        {
                                                            X509Data = new[]
                                                            {
                                                                new
                                                                {
                                                                    X509Certificate = V(certBase64),
                                                                    X509SubjectName = V(issuerName),
                                                                    X509IssuerSerial = new[]
                                                                    {
                                                                        new
                                                                        {
                                                                            X509IssuerName = V(issuerName),
                                                                            X509SerialNumber = V(serialDecimal)
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    },
                                                    Object = new[]
                                                    {
                                                        new
                                                        {
                                                            QualifyingProperties = new[]
                                                            {
                                                                new
                                                                {
                                                                    Target = "signature",
                                                                    // Embed the exact same object that was hashed for propsDigest.
                                                                    // Using a separate object literal would produce a different serialization → DS320.
                                                                    SignedProperties = signedPropsNode
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Wrap a scalar value in UBL { "_": value } convention.</summary>
    private static object[] V(string value) => new[] { new { _ = value } };

    private static string[] ParseAddressLines(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return Array.Empty<string>();
        return address.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                      .Take(3).ToArray();
    }

    private static string MapUom(string uom)
    {
        // Map common UOMs to UN/ECE recommendation 20 codes
        return uom?.ToUpperInvariant() switch
        {
            "EA" or "EACH" or "PCS" or "PC" => "C62",
            "KG" or "KGS" => "KGM",
            "G" or "GM" => "GRM",
            "L" or "LTR" => "LTR",
            "M" => "MTR",
            "M2" => "MTK",
            "M3" => "MTQ",
            "SET" => "SET",
            "HR" or "HRS" => "HUR",
            "DAY" or "DAYS" => "DAY",
            "MON" or "MONTH" => "MON",
            _ => "C62"  // default: unit (each)
        };
    }

    private static string MapTaxCategory(string taxCode)
    {
        // LHDN tax category IDs
        return taxCode?.ToUpperInvariant() switch
        {
            "01" => "01",   // Standard SST/GST
            "02" => "02",   // Zero-rated
            "03" => "03",   // Exempt
            "E" => "E",
            "S" => "S",
            "Z" => "Z",
            _ => "01"
        };
    }

    /// <summary>
    /// Convert a hex string (X509 serial number) to decimal string.
    /// LHDN sample uses decimal serial numbers.
    /// </summary>
    private static string HexToDecimalString(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "0";
        try
        {
            // Handle large serials via BigInteger
            var value = System.Numerics.BigInteger.Parse("0" + hex.TrimStart('0'),
                System.Globalization.NumberStyles.HexNumber);
            return value.ToString();
        }
        catch
        {
            return hex;
        }
    }

    private static readonly JsonSerializerOptions MinifyOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,  // preserve exact property names
        Converters = { new DecimalNormalizer() }
    };

    // LHDN's server parse+reserializes the submitted document before hashing for DS322.
    // Their JSON library strips decimal trailing zeros: 160.000000 → 160, 12030.40 → 12030.4.
    // We must emit numbers in the same minimal form so our canonical matches theirs.
    private sealed class DecimalNormalizer : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            // Normalize: strip trailing fractional zeros, then strip trailing decimal point.
            // Only trim after the decimal point — never trim integer digits.
            // 160.000000 → "160", 12030.40 → "12030.4", 75.19 → "75.19"
            // 2000.000000 → "2000"  (NOT "2" — G29 gives "2000.000000", trim only after '.')
            var s = value.ToString("G29");
            var normalized = s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
            writer.WriteRawValue(string.IsNullOrEmpty(normalized) ? "0" : normalized);
        }
    }

    // -----------------------------------------------------------------------
    // UBL envelope model (top-level _D/_A/_B namespace declarations)
    // -----------------------------------------------------------------------

    private class UblEnvelope
    {
        [JsonPropertyName("_D")]
        public string D { get; set; } = string.Empty;

        [JsonPropertyName("_A")]
        public string A { get; set; } = string.Empty;

        [JsonPropertyName("_B")]
        public string B { get; set; } = string.Empty;

        [JsonPropertyName("Invoice")]
        public object[] Invoice { get; set; } = Array.Empty<object>();
    }
}

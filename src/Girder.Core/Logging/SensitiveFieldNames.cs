namespace Girder.Core.Logging;

/// <summary>
/// Field names whose values must not reach a log.
/// </summary>
/// <remarks>
/// One list, because there are two paths a value can take into a log — the CQRS
/// behaviour writing a whole command, and the HTTP middleware writing a whole
/// body — and two lists would agree on the day they were written and never
/// again.
/// <para>
/// Matched <b>exactly</b>, never as a substring: <c>RequestName</c> and
/// <c>ServiceName</c> name software, not people, and a log with those redacted
/// is one nobody can follow.
/// </para>
/// </remarks>
public static class SensitiveFieldNames
{
    private static readonly string[] Names =
    [
        "password", "pwd", "pass", "passwd", "secret", "token",
        "apikey", "api_key", "api-key", "authorization", "auth", "bearer",
        "creditcard", "credit_card", "credit-card", "cc", "cardnumber", "card_number",
        "cvv", "cvc", "securitycode", "security_code", "ssn", "socialsecuritynumber",
        "social_security_number", "email", "emailaddress", "email_address", "phone", "phonenumber",
        "phone_number", "mobile", "birthdate", "birth_date", "dob", "dateofbirth",
        "bankaccount", "bank_account", "accountnumber", "account_number", "routingnumber", "routing_number",
        "connectionstring", "connection_string", "privatekey", "private_key", "publickey", "public_key",
        "accesstoken", "access_token", "refreshtoken", "refresh_token", "otp", "verificationcode",
        "verification_code", "name", "fullname", "full_name",
        "displayname", "display_name", "firstname", "first_name", "givenname", "given_name",
        "lastname", "last_name", "surname", "familyname", "family_name", "middlename",
        "middle_name", "nickname", "maidenname", "maiden_name", "username", "user_name",
        "login", "street", "streetaddress", "street_address", "address", "postcode",
        "postalcode", "postal_code", "zip", "zipcode", "zip_code", "city",
        "housenumber", "house_number", "iban", "bic", "taxid", "tax_id",
        "vatid", "vat_id", "passportnumber", "passport_number", "idcardnumber", "id_card_number"
    ];

    /// <summary>The names, for an exact case-insensitive match.</summary>
    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(Names, StringComparer.OrdinalIgnoreCase);

    /// <summary>The names as a regex alternation, for matching JSON text.</summary>
    public static string Alternation { get; } = string.Join("|", Names);
}

using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public static class ServiceKeySchema
{
    public static List<CredentialField> GetSchema() =>
    [
        // --- Stripe ---
        new()
        {
            Key = "stripe_publishable_key",
            Label = "Stripe Publishable Key",
            Type = "text",
            Sensitive = true,
            Required = true,
            Placeholder = "pk_live_...",
            Help = new()
            {
                Summary = "Public key used by the frontend to create Stripe Checkout sessions",
                Steps =
                [
                    "Log in to dashboard.stripe.com",
                    "Navigate to Developers \u2192 API Keys",
                    "Copy the \"Publishable key\" (starts with pk_live_ or pk_test_)"
                ],
                Url = "https://dashboard.stripe.com/apikeys"
            },
            Validation = [new() { Type = "pattern", Value = @"^pk_(test|live)_", Message = "Must start with pk_test_ or pk_live_" }]
        },
        new()
        {
            Key = "stripe_secret_key",
            Label = "Stripe Secret Key",
            Type = "password",
            Sensitive = true,
            Required = true,
            Placeholder = "sk_live_...",
            Help = new()
            {
                Summary = "Secret key used by the hub server to manage subscriptions and billing",
                Steps =
                [
                    "Log in to dashboard.stripe.com",
                    "Navigate to Developers \u2192 API Keys",
                    "Click \"Reveal live key\" (or \"Reveal test key\")",
                    "Copy the secret key (starts with sk_live_ or sk_test_)"
                ],
                Url = "https://dashboard.stripe.com/apikeys"
            },
            Validation = [new() { Type = "pattern", Value = @"^(sk|rk)_(test|live)_", Message = "Must start with sk_test_, sk_live_, rk_test_, or rk_live_" }]
        },

        // --- SMTP ---
        new()
        {
            Key = "smtp_host",
            Label = "SMTP Host",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "smtp.sendgrid.net",
            Help = new()
            {
                Summary = "SMTP server hostname for sending transactional email",
                Steps =
                [
                    "Sign up with an email provider (SendGrid, Mailgun, Amazon SES, etc.)",
                    "Find the SMTP hostname in your provider's settings",
                    "Common values: smtp.sendgrid.net, smtp.mailgun.org, email-smtp.us-east-1.amazonaws.com"
                ]
            },
            Validation = [new() { Type = "minLength", Value = "3", Message = "SMTP host must be at least 3 characters" }]
        },
        new()
        {
            Key = "smtp_port",
            Label = "SMTP Port",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "587",
            Help = new()
            {
                Summary = "SMTP server port number",
                Steps =
                [
                    "Use 587 for STARTTLS (recommended)",
                    "Use 465 for implicit TLS",
                    "Use 25 only if your provider requires it (often blocked by cloud providers)"
                ]
            },
            Validation = [new() { Type = "pattern", Value = @"^\d{2,5}$", Message = "Must be a valid port number" }]
        },
        new()
        {
            Key = "smtp_username",
            Label = "SMTP Username",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "apikey",
            Help = new()
            {
                Summary = "SMTP authentication username",
                Steps =
                [
                    "Find SMTP credentials in your email provider's dashboard",
                    "For SendGrid: the username is literally \"apikey\"",
                    "For Mailgun: typically postmaster@your-domain.com",
                    "For Amazon SES: the SMTP IAM credential username"
                ]
            },
            Validation = [new() { Type = "minLength", Value = "1", Message = "SMTP username is required" }]
        },
        new()
        {
            Key = "smtp_password",
            Label = "SMTP Password",
            Type = "password",
            Sensitive = true,
            Required = true,
            Placeholder = "Enter SMTP password or API key",
            Help = new()
            {
                Summary = "SMTP authentication password or API key",
                Steps =
                [
                    "Find SMTP credentials in your email provider's dashboard",
                    "For SendGrid: use your API key as the password",
                    "For Mailgun: use the SMTP password from your domain settings",
                    "For Amazon SES: use the SMTP IAM credential secret"
                ]
            },
            Validation = [new() { Type = "minLength", Value = "1", Message = "SMTP password is required" }]
        },
        new()
        {
            Key = "smtp_from_address",
            Label = "From Address",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "noreply@example.com",
            Help = new()
            {
                Summary = "Sender email address for outgoing mail",
                Steps =
                [
                    "Use an address at your deployment domain (e.g. noreply@yourdomain.com)",
                    "The address must be verified with your email provider",
                    "For SendGrid: verify under Settings \u2192 Sender Authentication",
                    "For Mailgun: verify under Sending \u2192 Domains"
                ]
            },
            Validation = [new() { Type = "pattern", Value = @"^[^@\s]+@[^@\s]+\.[^@\s]+$", Message = "Must be a valid email address" }]
        },
        new()
        {
            Key = "smtp_from_name",
            Label = "From Name",
            Type = "text",
            Sensitive = false,
            Required = false,
            Placeholder = "Xcord",
            Help = new()
            {
                Summary = "Display name shown in the \"From\" header of outgoing emails",
                Steps =
                [
                    "Choose a recognizable name for your deployment",
                    "This appears as the sender name in email clients",
                    "Leave blank to use just the email address"
                ]
            }
        },

        // --- Tenor ---
        new()
        {
            Key = "tenor_api_key",
            Label = "Tenor API Key",
            Type = "password",
            Sensitive = true,
            Required = false,
            Placeholder = "AIza...",
            Help = new()
            {
                Summary = "Google Tenor API key for GIF search in chat (optional \u2014 GIF search disabled without it)",
                Steps =
                [
                    "Go to console.cloud.google.com",
                    "Create a project (or select an existing one)",
                    "Navigate to APIs & Services \u2192 Library",
                    "Search for \"Tenor API\" and enable it",
                    "Go to APIs & Services \u2192 Credentials",
                    "Click \"Create Credentials\" \u2192 \"API Key\"",
                    "Copy the generated key"
                ],
                Url = "https://console.cloud.google.com/apis/library/tenor.googleapis.com"
            },
            Validation = [new() { Type = "minLength", Value = "10", Message = "API key must be at least 10 characters" }]
        }
    ];
}

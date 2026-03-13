using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public static class ServiceKeySchema
{
    public static List<CredentialField> GetSchema(Topology? topology = null)
    {
        var fields = GetBaseSchema();

        if (topology?.BackupTarget != null)
        {
            var needsManualCreds = topology.BackupTarget.Kind == BackupTargetKind.S3Compatible;
            fields.AddRange(GetColdStoreSchema(needsManualCreds));
        }

        return fields;
    }

    private static List<CredentialField> GetBaseSchema() =>
    [
        // --- Docker Registry ---
        new()
        {
            Key = "registry_url",
            Label = "Registry URL",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "docker.xcord.net",
            Help = new()
            {
                Summary = "Docker registry URL that all xcord images are pulled from during provisioning",
                Steps =
                [
                    "If your topology includes a Registry node, use its configured domain",
                    "Otherwise, use the URL of any Docker-compatible registry (Docker Hub, GHCR, ECR, etc.)",
                    "All hosts will run 'docker login' against this URL before pulling images"
                ]
            },
            Validation = [new() { Type = "minLength", Value = "3", Message = "Registry URL must be at least 3 characters" }]
        },
        new()
        {
            Key = "registry_username",
            Label = "Registry Username",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "admin",
            Help = new()
            {
                Summary = "Username for authenticating with the Docker registry",
                Steps =
                [
                    "For a self-hosted registry, choose a username for htpasswd auth",
                    "For Docker Hub, use your Docker Hub username",
                    "For GHCR, use your GitHub username",
                    "For ECR, use 'AWS' as the username"
                ]
            },
            Validation = [new() { Type = "minLength", Value = "1", Message = "Registry username is required" }]
        },
        new()
        {
            Key = "registry_password",
            Label = "Registry Password",
            Type = "password",
            Sensitive = true,
            Required = true,
            Placeholder = "Enter registry password or token",
            Help = new()
            {
                Summary = "Password or access token for authenticating with the Docker registry",
                Steps =
                [
                    "For a self-hosted registry, choose a strong password for htpasswd auth",
                    "For Docker Hub, use a Personal Access Token (not your account password)",
                    "For GHCR, use a GitHub PAT with read:packages scope",
                    "For ECR, use the output of 'aws ecr get-login-password'"
                ]
            },
            Validation = [new() { Type = "minLength", Value = "1", Message = "Registry password is required" }]
        },

        // --- Stripe ---
        new()
        {
            Key = "stripe_publishable_key",
            Label = "Stripe Publishable Key",
            Type = "text",
            Sensitive = true,
            Required = false,
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
            Required = false,
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
            Required = false,
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

        // --- Hub Admin & Domain ---
        new()
        {
            Key = "hub_admin_username",
            Label = "Admin Username",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "admin",
            Help = new()
            {
                Summary = "Username for the hub admin account created on first boot",
                Steps =
                [
                    "Choose a username for the hub administrator",
                    "This account has full control over the hub and instance provisioning"
                ]
            }
        },
        new()
        {
            Key = "hub_admin_email",
            Label = "Admin Email",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "admin@example.com",
            Help = new()
            {
                Summary = "Email address for the hub admin account",
                Steps =
                [
                    "Enter the email address for the hub administrator",
                    "Used for login and password recovery"
                ]
            },
            Validation = [new() { Type = "pattern", Value = "^[^@]+@[^@]+\\.[^@]+$", Message = "Must be a valid email address" }]
        },
        new()
        {
            Key = "hub_base_domain",
            Label = "Hub Base Domain",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "xcord.net",
            Help = new()
            {
                Summary = "The public domain where the hub is accessible (e.g. xcord.net)",
                Steps =
                [
                    "Enter the domain name pointed at your deployment",
                    "This is used for JWT issuer, CORS origins, and email links"
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
        },

    ];

    private static List<CredentialField> GetColdStoreSchema(bool requireCredentials) =>
    [
        new()
        {
            Key = "coldstore_access_key",
            Label = "Cold Storage Access Key",
            Type = "text",
            Sensitive = true,
            Required = requireCredentials,
            Placeholder = "AKIAIOSFODNN7EXAMPLE",
            Help = new()
            {
                Summary = "S3-compatible access key for the cold storage backup bucket",
                Steps = requireCredentials
                    ? ["Create an application key in your S3-compatible provider dashboard"]
                    : ["Auto-provisioned by Terraform for your cloud provider - leave blank unless overriding"]
            },
            Validation = requireCredentials
                ? [new() { Type = "minLength", Value = "1", Message = "Access key is required" }]
                : []
        },
        new()
        {
            Key = "coldstore_secret_key",
            Label = "Cold Storage Secret Key",
            Type = "password",
            Sensitive = true,
            Required = requireCredentials,
            Placeholder = "Enter S3-compatible secret key",
            Help = new()
            {
                Summary = "S3-compatible secret key for the cold storage backup bucket",
                Steps = requireCredentials
                    ? ["Use the application key secret from your S3-compatible provider dashboard"]
                    : ["Auto-provisioned by Terraform for your cloud provider - leave blank unless overriding"]
            },
            Validation = requireCredentials
                ? [new() { Type = "minLength", Value = "1", Message = "Secret key is required" }]
                : []
        },
        new()
        {
            Key = "coldstore_endpoint",
            Label = "Cold Storage Endpoint",
            Type = "text",
            Sensitive = false,
            Required = requireCredentials,
            Placeholder = "s3.wasabisys.com",
            Help = new()
            {
                Summary = "S3-compatible endpoint URL for cold storage",
                Steps = requireCredentials
                    ?
                    [
                        "For Wasabi: s3.wasabisys.com (or region-specific, e.g., s3.us-west-1.wasabisys.com)",
                        "For Backblaze B2: s3.us-west-001.backblazeb2.com (check your bucket's endpoint)"
                    ]
                    : ["Auto-populated from Terraform outputs - leave blank unless overriding"]
            }
        },
        new()
        {
            Key = "coldstore_bucket",
            Label = "Cold Storage Bucket",
            Type = "text",
            Sensitive = false,
            Required = true,
            Placeholder = "xcord-backups",
            Help = new()
            {
                Summary = "S3 bucket name for storing backups",
                Steps = requireCredentials
                    ? ["The bucket must already exist in your S3-compatible provider"]
                    : ["Terraform will create this bucket - enter the desired name"]
            },
            Validation = [new() { Type = "minLength", Value = "3", Message = "Bucket name must be at least 3 characters" }]
        }
    ];
}

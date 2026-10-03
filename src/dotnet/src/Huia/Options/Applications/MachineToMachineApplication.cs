namespace Huia.Options;

/// <summary>
/// A confidential service-to-service application using the OAuth 2.0 client credentials grant.
/// </summary>
public class MachineToMachineApplication : HuiaApplication
{
    /// <inheritdoc />
    public override ClientKind Kind => ClientKind.MachineToMachine;

    /// <inheritdoc />
    public override bool IsPublic => false;

    /// <summary>The client secret required for machine-to-machine token requests.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Default constructor.</summary>
    public MachineToMachineApplication()
    {
    }

    /// <summary>Creates a machine-to-machine application with client id and secret.</summary>
    public MachineToMachineApplication(string clientId, string clientSecret) : base(clientId)
    {
        ClientSecret = clientSecret;
    }

    /// <inheritdoc />
    public override void Validate(string path, List<string> errors)
    {
        base.Validate(path, errors);

        errors.Require(!string.IsNullOrWhiteSpace(ClientSecret),
            HuiaOptionsValidation.Combine(path, nameof(ClientSecret)),
            "is required for a confidential client.");
    }
}

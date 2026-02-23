namespace Harvester.App.IBKR.Risk;

public enum FaRoutingStrictness
{
    Off,
    Warn,
    Reject
}

public sealed class FaRoutingValidator
{
    public IReadOnlyList<string> Validate(
        string masterAccount,
        string faOrderAccount,
        string faOrderGroup,
        string faOrderProfile,
        string faOrderMethod,
        string faOrderPercentage)
    {
        var issues = new List<string>();

        var hasGroup = !string.IsNullOrWhiteSpace(faOrderGroup);
        var hasProfile = !string.IsNullOrWhiteSpace(faOrderProfile);
        var hasMethod = !string.IsNullOrWhiteSpace(faOrderMethod);
        var hasPercentage = !string.IsNullOrWhiteSpace(faOrderPercentage);
        var hasExplicitAccount = !string.IsNullOrWhiteSpace(faOrderAccount);

        if (!hasGroup && !hasProfile)
        {
            issues.Add("FA route requires either fa-order-group or fa-order-profile.");
        }

        if (hasGroup && hasProfile)
        {
            issues.Add("FA route cannot include both fa-order-group and fa-order-profile.");
        }

        if (hasProfile && hasMethod)
        {
            issues.Add("FA profile route cannot include fa-order-method.");
        }

        if (hasProfile && hasPercentage)
        {
            issues.Add("FA profile route cannot include fa-order-percentage override.");
        }

        if (!hasGroup && hasMethod)
        {
            issues.Add("fa-order-method requires fa-order-group.");
        }

        if (!hasGroup && hasPercentage)
        {
            issues.Add("fa-order-percentage requires fa-order-group.");
        }

        if (hasExplicitAccount
            && !string.Equals(faOrderAccount.Trim(), masterAccount.Trim(), StringComparison.OrdinalIgnoreCase)
            && (hasGroup || hasProfile))
        {
            issues.Add("FA allocation routes must target the master account context; fa-order-account cannot be a different account when group/profile is used.");
        }

        return issues;
    }
}

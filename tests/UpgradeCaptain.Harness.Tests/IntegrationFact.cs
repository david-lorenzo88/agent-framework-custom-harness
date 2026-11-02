namespace UpgradeCaptain.Harness.Tests;

/// <summary>Test de integración: solo corre con UC_INTEGRATION=1 (usa dotnet, git y el feed de demo reales; nunca el modelo).</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("UC_INTEGRATION") != "1")
        {
            this.Skip = "Integración: exporta UC_INTEGRATION=1 (usa dotnet build/test y git reales sobre el repo de la demo).";
        }
    }
}

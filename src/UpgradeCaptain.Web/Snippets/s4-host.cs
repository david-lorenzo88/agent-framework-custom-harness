//@source src/UpgradeCaptain.Hosted/Program.cs
//en: The SAME factory as step 3, behind an Invocations host. In Foundry every session is a VM with its own
//en: $HOME, kept when the session scales to zero: all the state lives in $HOME/state.
//es: La MISMA factoría del step-3 detrás de un host Invocations. En Foundry cada sesión es una VM con su $HOME,
//es: que se conserva cuando la sesión escala a cero: todo el estado vive en $HOME/state.
UpgradeCaptainOptions options = UpgradeCaptainOptions.ForHosted(home); //!
// …
AgentHostBuilder host = AgentHost.CreateBuilder(args); //!
AddUpgradeCaptain(host.Services);
host.AddInvocations<UpgradeInvocationHandler>(); //! //en: POST /invocations · GET /readiness · port 8088 //es: POST /invocations · GET /readiness · puerto 8088
host.ConfigureTracing(tracing => tracing.AddSource(Telemetry.SourceName, "Experimental.Microsoft.Agents.AI")); //en: App Insights from the connection string Foundry injects (D-43) //es: App Insights con la cadena de conexión que inyecta Foundry (D-43)
host.Build().Run();

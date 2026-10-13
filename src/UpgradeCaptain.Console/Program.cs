// ─────────────────────────────────────────────────────────────────────────────
// step-0 · Chat básico
//
// Un ChatClientAgent "pelado": sin tools, sin sesión, sin historial.
// Cada RunAsync es una conversación nueva: el agente solo puede explicar,
// no puede mirar el repo ni recordar lo que le dijimos en el turno anterior.
// ─────────────────────────────────────────────────────────────────────────────

#pragma warning disable OPENAI001 // GetResponsesClient (Responses API) es experimental en el SDK de OpenAI.

using System.ClientModel.Primitives;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Core;
using static UpgradeCaptain.Core.Prompts;

DotEnv.Load(); // .env → FOUNDRY_PROJECT_ENDPOINT, FOUNDRY_MODEL, AZURE_TOKEN_CREDENTIALS=dev

string endpoint = DotEnv.Require("FOUNDRY_PROJECT_ENDPOINT");
string model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "gpt-5.4";

// 1. IChatClient sobre un proyecto de Microsoft Foundry (Responses API), como en los samples "Claw".
IChatClient chatClient =
    new AIProjectClient(
        new Uri(endpoint),
        new DefaultAzureCredential(),
        new AIProjectClientOptions { RetryPolicy = new ClientRetryPolicy(3) })
    .GetProjectOpenAIClient()
    .GetResponsesClient()
    .AsIChatClient(model);

// 2. Un agente que solo conversa: instrucciones y nada más.
AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new() { Instructions = Prompt },
});

// 3. Bucle de consola. Sin sesión: cada pregunta empieza de cero.
Console.WriteLine("Upgrade Captain · step-0 (chat básico). Escribe /exit para salir.");
while (true)
{
    Console.Write("\n> ");
    string? input = Console.ReadLine();
    if (input is null || input.Trim() == "/exit")
    {
        break;
    }

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    AgentResponse response = await agent.RunAsync(input);
    Console.WriteLine(response.Text);
}

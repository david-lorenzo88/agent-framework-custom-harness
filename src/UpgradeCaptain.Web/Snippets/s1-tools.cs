//@source src/UpgradeCaptain.Core/UpgradeTools.cs
//en: The 5 tools are plain C# methods. AIFunctionFactory turns them into tools the model can call;
//en: the description and the [Description] of each parameter are all the model knows about them.
//es: Las 5 tools son métodos C# normales. AIFunctionFactory los convierte en tools que el modelo puede llamar;
//es: la descripción y el [Description] de cada parámetro es todo lo que el modelo sabe de ellas.
AIFunctionFactory.Create(s.ReadSource, "read_source", //!
    "Returns the FULL content of a repository file as a single text. 64 KB maximum. " +
    "Every line starts with its number in the format '   N: ' (number, colon and a space): " +
    "strip that prefix to look at the code."),
// …
public string ReadSource(
    [Description("File path relative to the repository root, e.g. src/Canarias.Tours.Payments/FareCalculator.cs.")] string path)
    => repo.ReadSource(path);

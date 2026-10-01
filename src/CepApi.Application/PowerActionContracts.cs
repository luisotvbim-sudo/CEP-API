using System.ComponentModel.DataAnnotations;

namespace CepApi.Application;

public sealed record PowerActionCheckRequest([Required] string Action);
public sealed record PowerActionCheckResponse(string Action, string Decision, string Code, string Message, TimeAnalysisResponse? Analysis);

public static class PowerActionDecision
{
    public static PowerActionCheckResponse FromAnalysis(string action, TimeAnalysisResponse analysis)
    {
        if (analysis.Days.Any(x => x.Issues.Contains("above_tolerance")))
            return new(action, "blocked", "above_tolerance",
                "A diferença entre Monday e VR Mais ultrapassa a tolerância. Confira seus registros antes de continuar.", analysis);
        if (analysis.DeltaSeconds is null || analysis.Days.Count == 0 ||
            analysis.Days.Any(x => x.DeltaSeconds is null) ||
            analysis.Sources.Count != 2 || analysis.Sources.Any(x => x.Status != "complete"))
            return new(action, "indeterminate", "analysis_incomplete",
                "Não foi possível conferir suas horas. Verifique as fontes antes de continuar.", analysis);
        return new(action, "allowed", "within_tolerance", "Ação liberada: suas horas estão dentro da tolerância.", analysis);
    }
}

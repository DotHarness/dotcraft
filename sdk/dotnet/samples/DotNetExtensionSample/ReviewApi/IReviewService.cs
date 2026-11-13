namespace Acme.ReviewCore.Api;

/// <summary>The typed surface the provider plugin exports to its declared dependents.</summary>
public interface IReviewService
{
    /// <summary>Collapses runs of whitespace so review text compares cleanly.</summary>
    string Normalize(string text);

    /// <summary>Returns the checklist the provider wants every review to cover.</summary>
    IReadOnlyList<string> Checklist { get; }
}

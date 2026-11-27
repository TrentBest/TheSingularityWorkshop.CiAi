namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Transport-neutral identity for the one canonical v1 workflow-evidence payload. Receipts acknowledge
/// exact delivery bytes but never treat the payload as authorization.
/// </summary>
public interface ICiWorkflowEvidenceDelivery
{
    string DeliveryId { get; }
    string OperationId { get; }
    string IdempotencyKey { get; }
    string Payload { get; }
    string PayloadSha256 { get; }
}

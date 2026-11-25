namespace TheSingularityWorkshop.CiAi;

/// <summary>
/// Transport-neutral identity for a durable workflow-evidence payload. The same receipt ledger
/// can acknowledge v1 and v2 deliveries without treating either payload schema as authority.
/// </summary>
public interface ICiWorkflowEvidenceDelivery
{
    string DeliveryId { get; }
    string IdempotencyKey { get; }
    string Payload { get; }
    string PayloadSha256 { get; }
}

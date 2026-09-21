namespace CepApi.Domain;

public enum OrganizationStatus { Active, Suspended, Archived }
public enum UserStatus { Active, Suspended, Archived }
public enum UserRole { SystemAdmin, OrganizationAdmin, User }
public enum Product { Revit, Zwcad }
public enum TeamAssignmentRole { Member, Manager }
public enum ExternalWorkforceSource { Monday, VrMais }
public enum WorkforceSyncStatus { Running, Succeeded, PartiallySucceeded, Failed }

using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Messages;

/// <summary>
/// A message that is sent when a request is made to edit a connection profile.
/// </summary>
public class EditProfileMessage
{
    public VCenterConnection ProfileToEdit { get; }

    public EditProfileMessage (VCenterConnection profileToEdit)
    {
        ProfileToEdit = profileToEdit;
    }
}
namespace Urho3D.Core;

public abstract partial class Object
{
    /// <summary>
    /// Adds the slots of the RTTI overrides that the RTTI source generator emits.
    /// The generator of <c>IsManagedOverride</c> cannot see the output of another source generator,
    /// so it does not list these overrides.
    /// </summary>
    protected override bool IsManagedOverride(string slotKey) =>
        IsGeneratedManagedOverride(slotKey) || base.IsManagedOverride(slotKey);

    /// <summary>
    /// Determines whether a source generator emitted a managed override of the virtual method with the given slot key.
    /// The RTTI source generator overrides it for each managed type.
    /// </summary>
    /// <param name="slotKey">The slot key of the virtual method.</param>
    /// <returns><see langword="true"/> if generated code overrides the virtual method.</returns>
    protected virtual bool IsGeneratedManagedOverride(string slotKey) => false;
}

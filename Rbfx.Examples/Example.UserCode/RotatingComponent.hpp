#pragma once

#include <Urho3D/Container/Str.h>
#include <Urho3D/Core/Context.h>
#include <Urho3D/Scene/LogicComponent.h>

namespace RbfxExamples
{

/// Logic component that rotates its node around the Y axis.
/// The URHO3D_OBJECT type information gives the managed wrapper the static GetTypeStatic accessor,
/// so the managed side creates the component with Node.CreateComponent<RotatingComponent>().
class RotatingComponent : public Urho3D::LogicComponent
{
    URHO3D_OBJECT(RotatingComponent, Urho3D::LogicComponent);

public:
    /// Registers the object factory. The managed side calls this method once for a context.
    static void RegisterObject(Urho3D::Context* context);

    explicit RotatingComponent(Urho3D::Context* context);

    void SetRotationSpeed(float degreesPerSecond);
    float GetRotationSpeed() const;
    int GetUpdateCount() const;
    /// Returns a text that describes the component. The string returns by value.
    ea::string Describe() const;

    void Update(float timeStep) override;

private:
    float rotationSpeed_{45.0f};
    int updateCount_{};
};

} // namespace RbfxExamples

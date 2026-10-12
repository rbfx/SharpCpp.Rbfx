#pragma once

#include <Urho3D/Core/Context.h>
#include <Urho3D/Scene/LogicComponent.h>

namespace RbfxExamples
{

/// Logic component that rotates its node around the Y axis.
/// The class has no URHO3D_OBJECT. The bindings of user code cannot wrap the type information
/// accessors of that macro. The factory type RotatingComponentImpl in RotatingComponent.cpp adds them.
class RotatingComponent : public Urho3D::LogicComponent
{
public:
    explicit RotatingComponent(Urho3D::Context* context);

    void SetRotationSpeed(float degreesPerSecond);
    float GetRotationSpeed() const;
    int GetUpdateCount() const;

    void Update(float timeStep) override;

private:
    float rotationSpeed_{45.0f};
    int updateCount_{};
};

} // namespace RbfxExamples

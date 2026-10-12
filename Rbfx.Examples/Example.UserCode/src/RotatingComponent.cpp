#include <RbfxExamples/RotatingComponent.hpp>

#include <Urho3D/Scene/Node.h>

namespace RbfxExamples
{

RotatingComponent::RotatingComponent(Urho3D::Context* context)
    : LogicComponent(context)
{
    SetUpdateEventMask(Urho3D::UpdateEvent::USE_UPDATE);
}

void RotatingComponent::SetRotationSpeed(float degreesPerSecond)
{
    rotationSpeed_ = degreesPerSecond;
}

float RotatingComponent::GetRotationSpeed() const
{
    return rotationSpeed_;
}

int RotatingComponent::GetUpdateCount() const
{
    return updateCount_;
}

void RotatingComponent::Update(float timeStep)
{
    ++updateCount_;
    if (Urho3D::Node* node = GetNode())
        node->Rotate(Urho3D::Quaternion(rotationSpeed_ * timeStep, Urho3D::Vector3::UP));
}

namespace
{

/// Creatable type of RotatingComponent. It owns the type information that the object factory needs.
class RotatingComponentImpl final : public RotatingComponent
{
    URHO3D_OBJECT(RotatingComponentImpl, RotatingComponent);

public:
    using RotatingComponent::RotatingComponent;
};

} // namespace

} // namespace RbfxExamples

#if defined(_WIN32)
#define RBFX_EXAMPLES_EXPORT extern "C" __declspec(dllexport)
#else
#define RBFX_EXAMPLES_EXPORT extern "C" __attribute__((visibility("default")))
#endif

/// Creates a RotatingComponent on a node. The managed bindings of user code cannot pass Node or
/// Context pointers, so the managed side calls this function with the node handle.
/// Returns the component.
RBFX_EXAMPLES_EXPORT void* RbfxExamples_AddRotatingComponent(void* nodeHandle, float degreesPerSecond)
{
    auto* node = static_cast<Urho3D::Node*>(nodeHandle);
    if (!node)
        return nullptr;

    Urho3D::Context* context = node->GetContext();
    if (!context->GetReflection(RbfxExamples::RotatingComponentImpl::GetTypeStatic()))
        context->AddFactoryReflection<RbfxExamples::RotatingComponentImpl>();

    auto* component = node->CreateComponent<RbfxExamples::RotatingComponentImpl>();
    component->SetRotationSpeed(degreesPerSecond);
    return static_cast<RbfxExamples::RotatingComponent*>(component);
}

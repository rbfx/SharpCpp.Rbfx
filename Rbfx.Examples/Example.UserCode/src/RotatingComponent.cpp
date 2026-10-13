#include <RbfxExamples/RotatingComponent.hpp>

#include <Urho3D/Scene/Node.h>

namespace RbfxExamples
{

RotatingComponent::RotatingComponent(Urho3D::Context* context)
    : LogicComponent(context)
{
    SetUpdateEventMask(Urho3D::UpdateEvent::USE_UPDATE);
}

void RotatingComponent::RegisterObject(Urho3D::Context* context)
{
    if (!context->GetReflection(GetTypeStatic()))
        context->AddFactoryReflection<RotatingComponent>();
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

} // namespace RbfxExamples

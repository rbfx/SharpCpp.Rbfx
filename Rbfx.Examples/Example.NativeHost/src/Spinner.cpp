#include <RbfxNativeHost/Spinner.hpp>

#include <Urho3D/Scene/Node.h>

namespace RbfxNativeHost
{

Spinner::Spinner(Urho3D::Context* context)
    : LogicComponent(context)
{
    SetUpdateEventMask(Urho3D::UpdateEvent::USE_UPDATE);
}

void Spinner::SetSpeed(float degreesPerSecond)
{
    speed_ = degreesPerSecond;
}

float Spinner::GetSpeed() const
{
    return speed_;
}

int Spinner::GetUpdateCount() const
{
    return updateCount_;
}

void Spinner::Update(float timeStep)
{
    ++updateCount_;
    if (Urho3D::Node* node = GetNode())
        node->Rotate(Urho3D::Quaternion(speed_ * timeStep, Urho3D::Vector3::UP));
}

HostInfo::HostInfo()
    : name_("Rbfx.NativeHost: C++ executable hosts the .NET runtime")
{
}

const ea::string& HostInfo::Describe() const
{
    return name_;
}

namespace
{

/// Creatable type of Spinner. It owns the type information that the object factory needs.
class SpinnerImpl final : public Spinner
{
    URHO3D_OBJECT(SpinnerImpl, Spinner);

public:
    using Spinner::Spinner;
};

} // namespace

} // namespace RbfxNativeHost

#if defined(_WIN32)
#define RBFX_NATIVE_HOST_EXPORT extern "C" __declspec(dllexport)
#else
#define RBFX_NATIVE_HOST_EXPORT extern "C" __attribute__((visibility("default")))
#endif

/// Creates a Spinner on a node. The managed bindings of user code cannot pass Node or Context
/// pointers, so the managed side calls this function with the node handle. Returns the Spinner.
RBFX_NATIVE_HOST_EXPORT void* RbfxNativeHost_AddSpinner(void* nodeHandle, float degreesPerSecond)
{
    auto* node = static_cast<Urho3D::Node*>(nodeHandle);
    if (!node)
        return nullptr;

    Urho3D::Context* context = node->GetContext();
    if (!context->GetReflection(RbfxNativeHost::SpinnerImpl::GetTypeStatic()))
        context->AddFactoryReflection<RbfxNativeHost::SpinnerImpl>();

    auto* spinner = node->CreateComponent<RbfxNativeHost::SpinnerImpl>();
    spinner->SetSpeed(degreesPerSecond);
    return static_cast<RbfxNativeHost::Spinner*>(spinner);
}

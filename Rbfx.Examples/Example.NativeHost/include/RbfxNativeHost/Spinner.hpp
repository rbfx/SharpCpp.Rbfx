#pragma once

#include <Urho3D/Container/RefCounted.h>
#include <Urho3D/Core/Context.h>
#include <Urho3D/Scene/LogicComponent.h>

#include <EASTL/string.h>

namespace RbfxNativeHost
{

/// Logic component that spins its node around the Y axis.
/// The class has no URHO3D_OBJECT. The bindings of user code cannot wrap the type information
/// accessors of that macro. The factory type SpinnerImpl in Spinner.cpp adds them.
class Spinner : public Urho3D::LogicComponent
{
public:
    explicit Spinner(Urho3D::Context* context);

    void SetSpeed(float degreesPerSecond);
    float GetSpeed() const;
    int GetUpdateCount() const;

    void Update(float timeStep) override;

private:
    float speed_{90.0f};
    int updateCount_{};
};

/// Small plain object that the managed side uses to show that wrapped C++ types work.
class HostInfo : public Urho3D::RefCounted
{
public:
    HostInfo();

    const ea::string& Describe() const;

private:
    ea::string name_;
};

} // namespace RbfxNativeHost

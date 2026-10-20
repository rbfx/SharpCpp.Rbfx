#pragma once

#include <utility>

#include <SharpCpp/ManagedCallback.hpp>
#include <SharpCpp/Marshaller.hpp>

#include <Urho3D/Container/RefCounted.h>
#include <Urho3D/Core/Signal.h>

namespace sharpcpp::interop::rbfx::detail
{
/// Reads the protected flag Detail::SignalBase::invocationInProgress_ of a signal.
/// A derived class can name the protected member. The pointer to member has the type
/// `bool Detail::SignalBase<...>::*`, so it applies to each object of the signal type.
/// The code never makes an object of this class.
template <class TSignal>
struct SignalInvocationAccess : TSignal
{
    static bool IsInProgress(const TSignal& signal) noexcept
    {
        return signal.*(&SignalInvocationAccess::invocationInProgress_);
    }
};

template <class TSignal>
bool IsSignalInvocationInProgress(const TSignal& signal) noexcept
{
    return SignalInvocationAccess<TSignal>::IsInProgress(signal);
}
}

namespace SharpCpp
{
/// Each instantiation of Urho3D::Signal becomes one non-generic wrapper class.
/// The managed code gets the field as a borrowed pointer handle.
/// The handler is a ManagedCallback (24 bytes). Signal::Subscribe wraps it in a lambda
/// that fits the 32-byte ea::fixed_function and returns true, so the subscription stays.
template <class Sender, class... Args>
struct [[sharpcpp::managed_type(
    public_type = "Signal",
    flatten_instantiations = true
)]] Marshaller<Urho3D::Signal<void(Args...), Sender>*>
{
    using signal_type = Urho3D::Signal<void(Args...), Sender>;
    using cpp_type = signal_type*;
    using abi_type = signal_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }

    /// The signal keeps a weak reference to the receiver. The subscription ends when the receiver expires.
    [[sharpcpp::expose_member]]
    static void Subscribe(Urho3D::Signal<void(Args...), Sender>& self, Urho3D::RefCounted* receiver, SharpCpp::ManagedCallback<void(Args...)> handler)
    {
        self.Subscribe(receiver, std::move(handler));
    }

    [[sharpcpp::expose_member]]
    static void SubscribeWithSender(Urho3D::Signal<void(Args...), Sender>& self, Urho3D::RefCounted* receiver, SharpCpp::ManagedCallback<void(Sender*, Args...)> handler)
    {
        self.SubscribeWithSender(receiver, std::move(handler));
    }

    [[sharpcpp::expose_member]]
    static void Unsubscribe(Urho3D::Signal<void(Args...), Sender>& self, Urho3D::RefCounted* receiver)
    {
        self.Unsubscribe(receiver);
    }

    /// The engine asserts on a nested invocation. Thus this member refuses it before the call.
    [[sharpcpp::expose_member(throw_on_false = "The signal is already being invoked.")]]
    static bool Invoke(Urho3D::Signal<void(Args...), Sender>& self, Sender* sender, Args... args)
    {
        if (sharpcpp::interop::rbfx::detail::IsSignalInvocationInProgress(self))
            return false;

        self(sender, args...);
        return true;
    }

    [[sharpcpp::expose_member]]
    static bool HasSubscriptions(Urho3D::Signal<void(Args...), Sender>& self)
    {
        return self.HasSubscriptions();
    }
};

/// Each instantiation of Urho3D::PrioritySignal becomes one non-generic wrapper class.
/// A subscription with a higher priority runs first.
template <class Sender, class Priority, class... Args>
struct [[sharpcpp::managed_type(
    public_type = "PrioritySignal",
    flatten_instantiations = true
)]] Marshaller<Urho3D::PrioritySignal<void(Args...), Priority, Sender>*>
{
    using signal_type = Urho3D::PrioritySignal<void(Args...), Priority, Sender>;
    using cpp_type = signal_type*;
    using abi_type = signal_type*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept { return value; }
    constexpr cpp_type ConvertToCpp(abi_type value) noexcept { return value; }

    [[sharpcpp::expose_member]]
    static void Subscribe(Urho3D::PrioritySignal<void(Args...), Priority, Sender>& self, Urho3D::RefCounted* receiver, Priority priority, SharpCpp::ManagedCallback<void(Args...)> handler)
    {
        self.Subscribe(receiver, priority, std::move(handler));
    }

    [[sharpcpp::expose_member]]
    static void SubscribeWithSender(Urho3D::PrioritySignal<void(Args...), Priority, Sender>& self, Urho3D::RefCounted* receiver, Priority priority, SharpCpp::ManagedCallback<void(Sender*, Args...)> handler)
    {
        self.SubscribeWithSender(receiver, priority, std::move(handler));
    }

    [[sharpcpp::expose_member]]
    static void Unsubscribe(Urho3D::PrioritySignal<void(Args...), Priority, Sender>& self, Urho3D::RefCounted* receiver)
    {
        self.Unsubscribe(receiver);
    }

    /// The engine asserts on a nested invocation. Thus this member refuses it before the call.
    [[sharpcpp::expose_member(throw_on_false = "The signal is already being invoked.")]]
    static bool Invoke(Urho3D::PrioritySignal<void(Args...), Priority, Sender>& self, Sender* sender, Args... args)
    {
        if (sharpcpp::interop::rbfx::detail::IsSignalInvocationInProgress(self))
            return false;

        self(sender, args...);
        return true;
    }

    [[sharpcpp::expose_member]]
    static bool HasSubscriptions(Urho3D::PrioritySignal<void(Args...), Priority, Sender>& self)
    {
        return self.HasSubscriptions();
    }
};
}

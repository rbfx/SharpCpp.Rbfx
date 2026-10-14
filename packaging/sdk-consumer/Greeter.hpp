#pragma once

#include <Urho3D/Container/Str.h>

namespace Consumer
{

class Greeter
{
public:
    explicit Greeter(int count) : count_(count) {}
    int GetCount() const { return count_; }
    void SetCount(int count) { count_ = count; }

private:
    int count_{};
};

ea::string Greet(const ea::string& name, int times);

} // namespace Consumer

#pragma once

#include <Urho3D/Container/Str.h>

namespace RbfxExamples
{

/// Plain C++ class without Urho3D::Object. It has a static method. It is in a second header of the example, so the
/// generator parses the headers through one umbrella header.
class TextTools
{
public:
    /// Returns a text of `length` bytes, repeated from `pattern`. The string returns by value.
    static ea::string RepeatText(const ea::string& pattern, int length);
};

} // namespace RbfxExamples

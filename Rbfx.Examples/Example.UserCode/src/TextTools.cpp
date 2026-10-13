#include <RbfxExamples/TextTools.hpp>

namespace RbfxExamples
{

ea::string TextTools::RepeatText(const ea::string& pattern, int length) const
{
    ea::string result;
    if (pattern.empty() || length <= 0)
        return result;
    result.reserve(static_cast<unsigned>(length));
    while (static_cast<int>(result.size()) < length)
        result.append(pattern);
    result.resize(static_cast<unsigned>(length));
    return result;
}

} // namespace RbfxExamples

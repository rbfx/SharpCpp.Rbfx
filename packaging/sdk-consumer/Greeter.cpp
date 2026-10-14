#include "Greeter.hpp"

namespace Consumer
{

ea::string Greet(const ea::string& name, int times)
{
    ea::string result;
    for (int i = 0; i < times; ++i)
        result += "Hello " + name + "\n";
    return result;
}

} // namespace Consumer

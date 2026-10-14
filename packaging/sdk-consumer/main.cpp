#include "Greeter.hpp"

int main()
{
    const Consumer::Greeter greeter(2);
    return Consumer::Greet("SDK", greeter.GetCount()).empty() ? 1 : 0;
}

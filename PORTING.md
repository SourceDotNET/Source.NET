This is a work in progress document!!

## Gotchas to Avoid

### Class/Struct from C++ to C#

In C++, the only difference between classes and structs are their default access levels. In C#, the difference between a class and a struct determines reference semantics. A class is always passed by reference, and a struct is mostly passed by value, but can be passed by reference (but not saved outside scope by reference)

When converting C++ to C#, always keep this in mind.

If C++ defined something as a class, but it seems to be exclusively used in stack-allocating contexts/as parts of an array, use structs, passing references to structs where necessary.
```cpp

class MyCoolClass {
    float m_flMyCoolValue;
    // ... etc ...
};

#define MAX_CLASSES 1024
MyCoolClass[MAX_CLASSES] Classes;

void DoSomethingWithThatClass(MyCoolClass *pClass) {
    pClass->m_flMyCoolValue = 0
}

void main() {
    DoSomethingWithThatClass(&Classes[8]);
}

```

In this (kind of poor) example, you can see how C++ defined this as a struct.
The same C# example below, without accounting for reference semantic differences:
```cs

public class MyCoolClass {
    public float MyCoolValue;
}

public static void DoSomethingWithThatClass(MyCoolClass instance) {
    instance.MyCoolValue = 0
}

public static void Main() {
    const int MAX_CLASSES = 1024;
    MyCoolClass[] classes = new MyCoolClass[MAX_CLASSES];
    DoSomethingWithThatClass(Classes[8]);
}

```

This is a waste of indirection, and on top of that, Classes[8] won't be instantiated (so this will error with a NullReferenceException). So each class would have to be instantiated in the array, and adds unnecessary garbage collection pressure.

The right way to do it in this case:

```cs

public struct MyCoolClass {
    public float MyCoolValue;
}

public static void DoSomethingWithThatClass(ref MyCoolClass instance) {
    instance.MyCoolValue = 0
}

public static void Main() {
    const int MAX_CLASSES = 1024;
    MyCoolClass[] classes = new MyCoolClass[MAX_CLASSES];
    DoSomethingWithThatClass(ref Classes[8]);
}

```

Note the ref keyword before the class name in the method argument, and the ref keyword before the argument in the call to the method. This would be equivalent to the original C++.

If you have the need to store a reference to a struct, you have a few options. Source.Common provides ``SafeFieldPointer`` when you're trying to store a reference to a known types field. 

In the case of the original array example, you can store a ``Memory<T>/T[]`` and the index, and then dereference it when needed. This is how ``BSPDWorldLightPtr`` works (see BSPFile.cs). It stores a BSPDWorldLight[] array and an nint Index. Call sites can then just call ``Dereference()`` on that struct, which returns ``ref Lights[Index]``.

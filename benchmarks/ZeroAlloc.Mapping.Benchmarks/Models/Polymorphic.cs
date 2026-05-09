namespace ZeroAlloc.Mapping.Benchmarks.Models;

public abstract record Animal(string Name);
public sealed record Dog(string Name, string Breed) : Animal(Name);
public sealed record Cat(string Name, bool Indoor) : Animal(Name);
public sealed record Bird(string Name, double Wingspan) : Animal(Name);

public abstract record AnimalDto(string Name);
public sealed record DogDto(string Name, string Breed) : AnimalDto(Name);
public sealed record CatDto(string Name, bool Indoor) : AnimalDto(Name);
public sealed record BirdDto(string Name, double Wingspan) : AnimalDto(Name);

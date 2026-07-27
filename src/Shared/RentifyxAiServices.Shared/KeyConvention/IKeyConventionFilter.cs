namespace RentifyxAiServices.SharedKernel.KeyConvention;

public interface IKeyConventionFilter
{
    bool Matches(string key);
}

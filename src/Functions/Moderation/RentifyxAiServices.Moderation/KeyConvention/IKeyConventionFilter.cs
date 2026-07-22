namespace RentifyxAiServices.Moderation.KeyConvention;

public interface IKeyConventionFilter
{
    bool Matches(string key);
}

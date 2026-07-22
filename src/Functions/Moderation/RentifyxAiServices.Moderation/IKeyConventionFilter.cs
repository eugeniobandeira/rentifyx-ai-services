namespace RentifyxAiServices.Moderation;

public interface IKeyConventionFilter
{
    bool Matches(string key);
}

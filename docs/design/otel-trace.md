## **OTEL Hierarchy**

1. **Activity**
2. **Span**
3. **Event** (An Activity can also have events)

## **Example: Add Dependency**

```csharp
services.AddSingleton(new ActivitySource("DAB"));
```

## **Example: Creating an Activity**

```csharp
public class RestController
{
    private readonly OtelRest _otel;

    public RestController(ActivitySource activitySource)
    {
        _otel = new(activitySource);
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<IActionResult> Find(string route)
    {
        _otel.StartRestActivity(
            (nameof(HttpContext.Request.Method), HttpContext.Request.Method),
            (nameof(EntityActionOperation), EntityActionOperation.Read.ToString()));

        return await HandleOperation(route, EntityActionOperation.Read);
    }

    private async Task<IActionResult> HandleOperation(
        string route, 
        EntityActionOperation operationType)
    {
        using (var spanValidateRequest = _otel.StartSpan("Validate Request Context"))
        {
            try
            {
                // Validate request context
                spanValidateRequest?.AddTags("valid", true);
            }
            catch (Exception ex)
            {
                _otel.RecordException(ex);
            }
        }

        using (var spanValidateEntity = _otel.StartSpan("Validate Entity Metadata"))
        {
            try
            {
                // Validate entity metadata
                spanValidateEntity?.AddTags("valid", true);
            }
            catch (Exception ex)
            {
                _otel.RecordException(ex);
            }
        }

        using (_otel.StartSpan("Authorize User"))
        {
            // Authorize user (policy)
        }

        using (var spanGetData = _otel.StartSpan("Get Data"))
        {
            _otel.AddEvent("Build Query");

            // Build query

            if (cache)
            {
                _otel.AddEvent("Use Cache");

                // Return cached data
            }
            else
            {
                using (var spanQueryDatabase = _otel.StartSpan("Query Database",
                    ("retry-number", retryCount)))
                {
                    try
                    {
                        _otel.AddEvent("Open Database", ("database-type", "mssql"));

                        // Open database connection

                        _otel.AddEvent("Query Database");

                        // Execute query
                    }
                    catch (Exception ex)
                    {
                        _otel.RecordException(ex);
                    }
                }
            }

            _otel.AddEvent("Format Results",
                ("rows", result.Length),
                ("size", result.Size));

            // Format & return results
        }

        return Ok();
    }
}
```

---

## **OTEL Helpers**

```csharp
public abstract class Otel
{
    private readonly ActivitySource _activitySource;
    private readonly string _activityName;

    protected Otel(ActivitySource activitySource, string activityName)
    {
        _activitySource = activitySource;
        _activityName = activityName;
    }

    public Activity? CurrentActivity { get; private set; }

    public virtual void StartActivity(params (string Name, string Value)[] tags)
    {
        CurrentActivity = _activitySource.StartActivity(_activityName, ActivityKind.Server);
        AddActivityTags(tags);
    }

    public void AddActivityTags(params (string Name, string Value)[] tags)
    {
        if (CurrentActivity == null) return;

        foreach (var tag in tags)
        {
            CurrentActivity.SetTag(tag.Name, tag.Value);
        }
    }

    public Activity? StartSpan(string name)
    {
        return _activitySource.StartActivity(name, ActivityKind.Internal, 
            CurrentActivity?.Context ?? default);
    }

    public void RecordException(Exception ex)
    {
        var activity = Activity.Current ?? CurrentActivity;
        if (activity == null) return;

        activity.SetTag("error", true);
        activity.SetTag("exception.type", ex.GetType().Name);
        activity.SetTag("exception.message", ex.Message);
    }

    public void AddEvent(string name, params (string Name, string Value)[] tags)
    {
        var activity = Activity.Current ?? CurrentActivity;
        if (activity == null) return;

        var tagCollection = new ActivityTagsCollection();
        foreach (var tag in tags)
        {
            tagCollection.Add(tag.Name, tag.Value);
        }

        activity.AddEvent(new ActivityEvent(name, default, tagCollection));
    }
}

public class OtelRest : Otel
{
    public OtelRest(ActivitySource activitySource) 
        : base(activitySource, "DAB.Rest") { }
}

public class OtelGraphQL : Otel
{
    public OtelGraphQL(ActivitySource activitySource) 
        : base(activitySource, "DAB.GraphQL") { }
}
```

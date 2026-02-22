namespace XcordTopo;

public interface IRequestHandler<in TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface IValidatable<in TRequest>
{
    Error? Validate(TRequest request);
}

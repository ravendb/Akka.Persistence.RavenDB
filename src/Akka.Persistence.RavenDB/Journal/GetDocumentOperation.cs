using System.Text;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations;
using Raven.Client.Http;
using Sparrow.Json;

namespace Akka.Persistence.RavenDb.Journal;

internal class GetDocumentOperation<T> : IMaintenanceOperation<T>
{
    private readonly string _id;
    private readonly string _node;

    public GetDocumentOperation(string id, string node)
    {
        _id = id;
        _node = node;
    }

    public RavenCommand<T> GetCommand(DocumentConventions conventions, JsonOperationContext context)
    {
        return new GetDocumentsCommand(_id, _node, conventions);
    }
    private class GetDocumentsCommand : RavenCommand<T>
    {
        private readonly string _id;
        private readonly DocumentConventions _conventions;

        public GetDocumentsCommand(string id, string node, DocumentConventions conventions)
        {
            _id = id;
            _conventions = conventions;
            SelectedNodeTag = node;
        }

        public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
        {
            var pathBuilder = new StringBuilder(node.Url);
            pathBuilder.Append("/databases/")
                .Append(node.Database)
                .Append("/docs?");

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get
            };

            pathBuilder.Append("&id=").Append(Uri.EscapeDataString(_id));

            url = pathBuilder.ToString();
            return request;
        }

        public override bool IsReadRequest => true;

        public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
        {
            if (response == null)
            {
                Result = default;
                return;
            }

            if (response.TryGet(nameof(GetDocumentsResult.Results), out BlittableJsonReaderArray results))
            {
                var result = (BlittableJsonReaderObject)results.Items.Single();
                Result = (T)_conventions.Serialization.DeserializeEntityFromBlittable(typeof(T), result);
            }
        }
    }
}
(function () {
  'use strict';

  function previewResource($http, umbRequestHelper) {

      var apiUrl = Umbraco.Sys.ServerVariables.TwentyFourDays.PreviewApi;

    var resource = {
      getPreview: getPreview
    };

    return resource;

    function getPreview(data, pageId, isGrid, culture) {

      return umbRequestHelper.resourcePromise(
          $http.post(apiUrl + '?pageId=' + pageId + '&isGrid=' + isGrid + '&culture=' + culture, data),
        'Failed getting preview markup'
      );
    };
  }

    angular.module('umbraco.resources').factory('TwentyFourDays.Resources.PreviewResource', previewResource);

})();

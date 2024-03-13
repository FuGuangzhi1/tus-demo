
if (!tus.isSupported) {
    alert('This browser does not support uploads. Please use a modern browser instead.')
}
var options, file, upload;

function startOrResumeUpload(upload) {
    if (!upload || !file) {
        alert('please select a file')
        return
    }
    // Check if there are any previous uploads to continue.
    upload.findPreviousUploads().then(function (previousUploads) {
        // Found previous uploads so we select the first one.
        if (previousUploads.length) {
            upload.resumeFromPreviousUpload(previousUploads[0])
        }

        // Start the upload
        upload.start()
    })
}

input.addEventListener('change', function (e) {
    // Get the selected file from the input element
    file = e.target.files[0]
    options = {
        endpoint: "http://localhost:1080/files/",
        metadata: {
            name: file.name,
            contentType: file.type || 'application/octet-stream',
            emptyMetaKey: '',
            data: JSON.stringify({ name: '额外参数' })
        },
        onShouldRetry: function (err, retryAttempt, options) {
            console.log({ err, retryAttempt, options, upload })
            var status = err.originalResponse ? err.originalResponse.getStatus() : 0
            // If the status is a 403, we do not want to retry.
            if (status === 403) {
                return false
            }

            // For any other status code, tus-js-client should retry.
            return true
        },
        // Callback for reporting upload progress
        onProgress: function (bytesUploaded, bytesTotal) {
            var percentage = ((bytesUploaded / bytesTotal) * 100).toFixed(2)
            uploadProgress.value = percentage;
            console.log(bytesUploaded, bytesTotal, percentage + '%')
        },
        onError: function (error) {
            console.log("Failed because: " + error)
        },
        onSuccess: function () {
            console.log("Download %s from %s", upload.file.name, upload.url)
        }
    }

    // Create the tus upload similar to the example from above
    upload = new tus.Upload(file, options)

})
//暂停
pauseButton.addEventListener("click", function () {
    if (!upload || !file) {
        alert('please select a file')
        return
    }
    upload.abort()
})
//开始上传 或者续传
unpauseButton.addEventListener("click", function () {
    startOrResumeUpload(upload)
})

const listingUrl = "{{ listingInfo.Url }}";

const addRepositoryButton = document.getElementById("add-repository");
const copyButton = document.getElementById("copy-url");
const listingUrlField = document.getElementById("listing-url");
const copyStatus = document.getElementById("copy-status");

addRepositoryButton.addEventListener("click", () => {
  window.location.href = `vcc://vpm/addRepo?url=${encodeURIComponent(listingUrl)}`;
});

copyButton.addEventListener("click", async () => {
  try {
    await navigator.clipboard.writeText(listingUrl);
    copyStatus.textContent = "Repository URL copied.";
  } catch {
    listingUrlField.select();
    copyStatus.textContent = "Copy was blocked. The repository URL is selected instead.";
  }
});

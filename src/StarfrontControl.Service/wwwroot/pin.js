// web/pin.ts
var form = document.getElementById("pin-form");
form.addEventListener("submit", async (event) => {
  event.preventDefault();
  const button = form.querySelector("button");
  button.disabled = true;
  try {
    const response = await fetch("/dashboard/auth/dashboard-login", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ code: document.getElementById("code").value }) });
    const result = await response.json();
    if (!response.ok)
      throw new Error(result.error || "Could not open dashboard");
    location.replace("/dashboard/");
  } catch (error) {
    document.getElementById("error").textContent = error instanceof Error ? error.message : "Unable to connect";
  } finally {
    button.disabled = false;
  }
});
